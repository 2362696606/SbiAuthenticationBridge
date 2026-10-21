using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// MOSIP-FTM 紧凑 payload 的编解码
/// </summary>
/// <remarks>
/// 请求是二进制，绝大多数响应是 UTF-8 JSON；GET_DKL1、LOAD_CERTIFICATE、SET_DKL1 的响应是
/// 固定长度的二进制帧，不能按 NUL 截断。
/// </remarks>
internal static class FtmProtocolCodec
{
    private const int CaptureHeaderLength = 63;

    private const int CapturePurposeAuth = 0;

    private const int CapturePurposeRegistration = 1;

    private const int SpecSbi095 = 0;

    private const int DeviceSubIdSingleFinger = 0;

    private const int MaxCertificateLength = 3500;

    private const int DklModulusLength = 256;

    private static readonly byte[] InitialPreviousHash =
        Convert.FromHexString("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

    private static readonly byte[] RsaExponent = [0x01, 0x00, 0x01];

    internal static byte[] EncodeDiscover(string callback, DateTimeOffset utc, FtmDeviceType deviceType)
    {
        var callbackBytes = EncodeCallback(callback);
        var payload = new byte[23 + callbackBytes.Length];

        payload[0] = 1;
        payload[1] = (byte)deviceType;
        WriteUtc(payload, 2, utc);
        payload[22] = (byte)callbackBytes.Length;
        callbackBytes.CopyTo(payload, 23);

        return payload;
    }

    internal static byte[] EncodeDeviceInfo(string callback, DateTimeOffset utc)
    {
        var callbackBytes = EncodeCallback(callback);
        var payload = new byte[22 + callbackBytes.Length];

        payload[0] = 1;
        WriteUtc(payload, 1, utc);
        payload[21] = (byte)callbackBytes.Length;
        callbackBytes.CopyTo(payload, 22);

        return payload;
    }

    internal static byte[] EncodeSetTime(DateTimeOffset utc)
    {
        var payload = new byte[21];
        payload[0] = 1;
        WriteUtc(payload, 1, utc);
        return payload;
    }

    internal static byte[] EncodeCapture(FtmCaptureRequest request, bool registration)
    {
        ValidateRange((int)request.Environment, 0, 3, nameof(request.Environment));
        ValidateRange((int)request.FingerPosition, 0, 10, nameof(request.FingerPosition));
        ValidateRange(request.BioCount, 0, 9, nameof(request.BioCount));
        ValidateRange(request.RequestedScore, 0, 100, nameof(request.RequestedScore));
        ValidateTransactionId(request.TransactionId);
        ValidateDeviceId(request.DeviceId);

        var domain = EncodeAscii(request.DomainUri, "domainUri",
            static c => c is not ('"' or '\\'), "不得含双引号或反斜杠");
        if (domain.Length > 192)
        {
            throw new FtmProtocolException($"domainUri 最长 192 字节，实际为 {domain.Length}。");
        }

        var transaction = Encoding.ASCII.GetBytes(request.TransactionId);
        var device = Encoding.ASCII.GetBytes(request.DeviceId);

        ReadOnlySpan<byte> previousHash = InitialPreviousHash;
        if (request.PreviousHash is { } memory)
        {
            previousHash = memory.Span;
        }

        if (previousHash.Length != 32)
        {
            throw new FtmProtocolException(
                $"previousHash 必须是 32 字节原始 SHA-256，实际为 {previousHash.Length} 字节。");
        }

        var payload = new byte[CaptureHeaderLength + domain.Length + transaction.Length + device.Length];

        payload[0] = 1;
        payload[1] = (byte)request.Environment;
        payload[2] = (byte)(registration ? CapturePurposeRegistration : CapturePurposeAuth);
        payload[3] = SpecSbi095;
        payload[4] = (byte)request.BioCount;
        payload[5] = (byte)request.FingerPosition;
        payload[6] = (byte)request.RequestedScore;
        payload[7] = DeviceSubIdSingleFinger;
        payload[8] = (byte)domain.Length;
        payload[9] = (byte)transaction.Length;
        payload[10] = (byte)device.Length;
        WriteUtc(payload, 11, request.Utc);
        previousHash.CopyTo(payload.AsSpan(31, 32));

        var offset = CaptureHeaderLength;
        domain.CopyTo(payload, offset);
        offset += domain.Length;
        transaction.CopyTo(payload, offset);
        offset += transaction.Length;
        device.CopyTo(payload, offset);

        return payload;
    }

    internal static byte[] EncodeGetDklPublicKey() => [1];

    internal static byte[] EncodeSetDklCertificate(ReadOnlySpan<byte> der)
    {
        if (der.Length is < 1 or > MaxCertificateLength)
        {
            throw new FtmProtocolException(
                $"DKL 证书 DER 长度须为 1..{MaxCertificateLength} 字节，实际为 {der.Length}。");
        }

        return der.ToArray();
    }

    internal static DiscoveryResponse ParseDiscoverResponse(ReadOnlySpan<byte> response)
    {
        using var document = ParseJsonDocument(response, "DISCOVER");
        var root = RootObject(document, "DISCOVER");

        return new DiscoveryResponse
        {
            DeviceId = ReadString(root, "deviceId"),
            DeviceStatus = ReadVerbatim(root, "deviceStatus"),
            Certification = ReadString(root, "certification"),
            ServiceVersion = ReadString(root, "serviceVersion"),
            DeviceSubId = ReadIntArray(root, "deviceSubId"),
            CallbackId = ReadString(root, "callbackId"),
            DigitalId = ReadString(root, "digitalId"),
            DeviceCode = ReadString(root, "deviceCode"),
            SpecVersion = ReadStringArray(root, "specVersion"),
            Purpose = ReadVerbatim(root, "purpose"),
            Error = ReadError(root)
        };
    }

    internal static DeviceInfoResponse ParseDeviceInfoResponse(ReadOnlySpan<byte> response)
    {
        using var document = ParseJsonDocument(response, "DEVICE_INFO");
        var root = RootObject(document, "DEVICE_INFO");

        return new DeviceInfoResponse
        {
            DeviceInfo = ReadString(root, "deviceInfo"),
            Error = ReadError(root)
        };
    }

    internal static string ReadResponseText(ReadOnlySpan<byte> response) =>
        Encoding.UTF8.GetString(TrimAtNul(response));

    internal static FtmDklPublicKeyResult ParseDklPublicKeyResult(ReadOnlySpan<byte> response)
    {
        const int headerLength = 9;
        const int modulusOffset = headerLength + 1;
        const int checksumLength = 2;
        const int frameLength = modulusOffset + DklModulusLength + checksumLength;

        if (response.Length < headerLength + 1)
        {
            throw new FtmProtocolException(
                "GET_DKL1_PUBLIC_KEY 响应不足 10 字节，读不到结果码。");
        }

        var resultCode = response[headerLength];
        if (resultCode != 0)
        {
            return new FtmDklPublicKeyResult { Result = ToDklResult(resultCode, "GET_DKL1_PUBLIC_KEY") };
        }

        if (response.Length < frameLength)
        {
            throw new FtmProtocolException(
                $"GET_DKL1_PUBLIC_KEY 响应不足 {frameLength} 字节，无法取出 {DklModulusLength} 字节模数。");
        }

        var payloadLength = (response[7] << 8) | response[8];
        if (payloadLength < 1 + DklModulusLength)
        {
            throw new FtmProtocolException(
                $"GET_DKL1_PUBLIC_KEY 帧的 payloadLen={payloadLength}，放不下结果码和 {DklModulusLength} 字节模数。");
        }

        var modulus = response.Slice(modulusOffset, DklModulusLength);

        return new FtmDklPublicKeyResult
        {
            Result = FtmDklResult.Ok,
            Key = new FtmDklPublicKey
            {
                RotationId = 1,
                SubjectPublicKeyInfo = BuildSubjectPublicKeyInfo(modulus)
            }
        };
    }

    internal static FtmDklResult ParseDklResult(ReadOnlySpan<byte> response)
    {
        if (response.Length < 2)
        {
            throw new FtmProtocolException("SET_DKL1_CERTIFICATE 响应不足 2 字节，读不到结果码。");
        }

        return ToDklResult(response[1], "SET_DKL1_CERTIFICATE");
    }

    internal static ReadOnlySpan<byte> TrimAtNul(ReadOnlySpan<byte> response)
    {
        var end = response.IndexOf((byte)0);
        return end < 0 ? response : response[..end];
    }

    private static byte[] BuildSubjectPublicKeyInfo(ReadOnlySpan<byte> modulus)
    {
        using var rsa = RSA.Create();

        try
        {
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = modulus.ToArray(),
                Exponent = RsaExponent
            });
        }
        catch (CryptographicException exception)
        {
            throw new FtmProtocolException(
                $"DKL1 公钥模数无法构成 RSA 公钥（{exception.Message}），响应帧可能为空或错位。");
        }

        return rsa.ExportSubjectPublicKeyInfo();
    }

    private static FtmDklResult ToDklResult(byte code, string operation) => code switch
    {
        0 => FtmDklResult.Ok,
        1 => FtmDklResult.InvalidData,
        2 => FtmDklResult.Busy,
        3 => FtmDklResult.RotateFailed,
        _ => throw new FtmProtocolException($"{operation} 返回未知结果码 0x{code:X2}。")
    };

    private static void WriteUtc(Span<byte> destination, int offset, DateTimeOffset utc)
    {
        var text = utc.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        if (text.Length != 20)
        {
            throw new FtmProtocolException(
                $"内部错误：格式化出的 UTC 为 {text.Length} 字节（\"{text}\"），应为 20。");
        }

        Encoding.ASCII.GetBytes(text, destination[offset..]);
    }

    private static byte[] EncodeCallback(string callback)
    {
        if (string.IsNullOrEmpty(callback))
        {
            throw new FtmProtocolException("callback 不能为空（长度须为 1..192 字节）。");
        }

        var bytes = EncodeAscii(callback, "callback",
            static c => c is not ('"' or '\\'), "不得含双引号或反斜杠");

        if (bytes.Length > 192)
        {
            throw new FtmProtocolException($"callback 最长 192 字节，实际为 {bytes.Length}。");
        }

        return bytes;
    }

    private static byte[] EncodeAscii(string value, string field, Func<char, bool> checks,
        string checkDescription)
    {
        foreach (var c in value)
        {
            if (c is < ' ' or > '~')
            {
                throw new FtmProtocolException($"{field} 含非可打印 ASCII 字符 U+{(int)c:X4}。");
            }

            if (!checks(c))
            {
                throw new FtmProtocolException($"{field} 含非法字符 '{c}'：{checkDescription}。");
            }
        }

        return Encoding.ASCII.GetBytes(value);
    }

    private static void ValidateRange(int value, int min, int max, string field)
    {
        if (value < min || value > max)
        {
            throw new FtmProtocolException($"{field} 必须在 {min}..{max} 之间，实际为 {value}。");
        }
    }

    private static void ValidateTransactionId(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            throw new FtmProtocolException("transactionId 不能为空。");
        }

        if (transactionId.Length is < 4 or > 50)
        {
            throw new FtmProtocolException(
                $"transactionId 长度须为 4..50，实际为 {transactionId.Length}。");
        }

        foreach (var c in transactionId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                throw new FtmProtocolException(
                    "transactionId 含非法字符：只允许 A-Z、a-z、0-9 与 '-'。");
            }
        }
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            throw new FtmProtocolException("deviceId 不能为空。");
        }

        if (deviceId.Length is not (12 or 20))
        {
            throw new FtmProtocolException(
                $"deviceId 长度只能是 20 或 12，实际为 {deviceId.Length}。");
        }
    }

    private static JsonDocument ParseJsonDocument(ReadOnlySpan<byte> response, string what)
    {
        var json = Encoding.UTF8.GetString(TrimAtNul(response));

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FtmProtocolException($"{what} 内容为空，没有可解析的 JSON。");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FtmProtocolException(
                $"{what} 内容不是合法 JSON：{exception.Message}；前 200 字符：\"{Truncate(json)}\"。");
        }
    }

    private static JsonElement RootObject(JsonDocument document, string operation)
    {
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                throw new FtmProtocolException($"{operation} 响应是空数组，没有设备对象。");
            }

            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FtmProtocolException(
                $"{operation} 响应的主体不是 JSON 对象（实际为 {root.ValueKind}）。");
        }

        return root;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadVerbatim(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static IReadOnlyList<int>? ReadIntArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var single))
        {
            return [single];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<int>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
            {
                items.Add(number);
            }
        }

        return items;
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return [value.GetString() ?? ""];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            items.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.GetRawText());
        }

        return items;
    }

    private static SbiError ReadError(JsonElement element)
    {
        if (!element.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return new SbiError { ErrorCode = "0", ErrorInfo = "Success" };
        }

        return new SbiError
        {
            ErrorCode = ReadVerbatim(error, "errorCode"),
            ErrorInfo = ReadString(error, "errorInfo")
        };
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "...";
}
