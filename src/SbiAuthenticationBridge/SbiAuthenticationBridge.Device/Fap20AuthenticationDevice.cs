using System.Globalization;
using System.Text.Json;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Device.Protocol;
using Serilog;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// FAP20 的 SBI 认证设备句柄：设备发现、设备信息与认证指纹采集
/// </summary>
public sealed class Fap20AuthenticationDevice : Fap20SbiDevice, IAuthenticationDevice
{
    private const string SuccessErrorCode = "0";

    private const string DefaultSpecVersion = "0.9.5";

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "FAP20");

    private static readonly Dictionary<string, FtmFingerPosition> FingerPositions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RightThumb"] = FtmFingerPosition.RightThumb,
            ["RightIndexFinger"] = FtmFingerPosition.RightIndexFinger,
            ["RightMiddleFinger"] = FtmFingerPosition.RightMiddleFinger,
            ["RightRingFinger"] = FtmFingerPosition.RightRingFinger,
            ["RightLittleFinger"] = FtmFingerPosition.RightLittleFinger,
            ["LeftThumb"] = FtmFingerPosition.LeftThumb,
            ["LeftIndexFinger"] = FtmFingerPosition.LeftIndexFinger,
            ["LeftMiddleFinger"] = FtmFingerPosition.LeftMiddleFinger,
            ["LeftRingFinger"] = FtmFingerPosition.LeftRingFinger,
            ["LeftLittleFinger"] = FtmFingerPosition.LeftLittleFinger
        };

    /// <summary>
    /// 创建 FAP20 的 SBI 认证设备句柄
    /// </summary>
    /// <param name="device">FAP20 设备实例</param>
    /// <param name="callbackId">
    /// SBI 设备服务回调地址，须为 1..192 字节可打印 ASCII
    /// </param>
    public Fap20AuthenticationDevice(FAP20 device, string callbackId)
        : base(device, callbackId)
    {
    }

    /// <summary>
    /// 认证指纹采集（CAPTURE_AUTH 0x20）
    /// </summary>
    /// <param name="request">认证采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 与请求中每个 <see cref="BioInfo"/> 一一对应的采集结果；
    /// 单项失败时在对应位置返回携带标准 SBI 错误的记录。
    /// </returns>
    public async Task<CaptureResponse> CaptureAsync(CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Logger.Information("开始认证采集，事务 {TransactionId:l}", request.TransactionId);

        var bios = request.Bio;
        if (bios is null || bios.Count == 0)
        {
            return CreateErrorResponse(1, () => SbiErrorHelper.RequestedBiometricCountNotSupported);
        }

        if (!IsValidTransactionId(request.TransactionId))
        {
            return CreateErrorResponse(bios.Count, () => SbiErrorHelper.InvalidTransactionId);
        }

        if (!TryMapEnvironment(request.Env, out var environment))
        {
            return CreateErrorResponse(bios.Count, () => SbiErrorHelper.TechnicalErrorDuringExtraction);
        }

        var deviceCode = await EnsureDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(deviceCode))
        {
            return CreateErrorResponse(bios.Count, () => SbiErrorHelper.DeviceNotReady);
        }

        var transactionId = request.TransactionId!;
        var timeoutMs = ResolveTimeout(request.Timeout);
        var captureTime = ResolveCaptureTime(request.CaptureTime);
        var biometrics = new List<BiometricInfo>(bios.Count);

        foreach (var bio in bios)
        {
            var biometric = await CaptureOneAsync(transactionId, request.DomainUri, bio, deviceCode,
                environment, captureTime, timeoutMs, cancellationToken).ConfigureAwait(false);
            biometrics.Add(biometric);
        }

        Logger.Information("认证采集完成，返回 {Count} 条结果", biometrics.Count);
        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 采集单个 bio 项
    /// </summary>
    private async Task<BiometricInfo> CaptureOneAsync(string transactionId, string? domainUri,
        BioInfo bio, string deviceCode, FtmEnvironment environment, DateTimeOffset captureTime,
        uint timeoutMs, CancellationToken cancellationToken)
    {
        var validationError = ValidateBio(bio, out var fingerPosition, out var requestedScore,
            out var previousHash);
        if (validationError is not null)
        {
            return new BiometricInfo { Error = validationError };
        }

        var ftmRequest = new FtmCaptureRequest
        {
            TransactionId = transactionId,
            DeviceId = deviceCode,
            Utc = captureTime,
            FingerPosition = fingerPosition,
            Environment = environment,
            BioCount = 1,
            RequestedScore = requestedScore,
            DomainUri = domainUri ?? string.Empty,
            PreviousHash = previousHash
        };

        try
        {
            var response = await Device.CaptureAuthAsync(ftmRequest, timeoutMs, cancellationToken)
                .ConfigureAwait(false);

            return ParseBiometric(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (FtmCommandException exception) when (exception.ResultCode == FAP20.DeviceBusyResultCode)
        {
            Logger.Warning(exception, "认证采集设备忙");
            return new BiometricInfo { Error = SbiErrorHelper.DeviceBusy };
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "认证采集失败");
            return new BiometricInfo
            {
                Error = Device.IsConnected
                    ? SbiErrorHelper.TechnicalErrorDuringExtraction
                    : SbiErrorHelper.DeviceNotReady
            };
        }
    }

    /// <summary>
    /// 校验并转换单个 bio 项的采集参数
    /// </summary>
    private static SbiError? ValidateBio(BioInfo bio, out FtmFingerPosition fingerPosition,
        out int requestedScore, out ReadOnlyMemory<byte>? previousHash)
    {
        fingerPosition = FtmFingerPosition.Unknown;
        requestedScore = FtmCaptureRequest.DefaultRequestedScore;
        previousHash = null;

        if (!string.IsNullOrEmpty(bio.Type)
            && !string.Equals(bio.Type, "Finger", StringComparison.OrdinalIgnoreCase))
        {
            return SbiErrorHelper.DeviceNotFound;
        }

        var subTypes = bio.BioSubType;
        if (subTypes is { Count: > 1 })
        {
            return SbiErrorHelper.RequestedBiometricCountNotSupported;
        }

        var count = bio.Count ?? subTypes?.Count ?? 1;
        if (count != 1)
        {
            return SbiErrorHelper.RequestedBiometricCountNotSupported;
        }

        if (subTypes is { Count: 1 })
        {
            var name = subTypes[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                return SbiErrorHelper.TechnicalErrorDuringExtraction;
            }

            var normalized = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!FingerPositions.TryGetValue(normalized, out fingerPosition))
            {
                if (!string.Equals(normalized, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                {
                    return SbiErrorHelper.TechnicalErrorDuringExtraction;
                }

                fingerPosition = FtmFingerPosition.Unknown;
            }
        }

        if (bio.RequestedScore is { } score)
        {
            if (double.IsNaN(score) || score is < 0 or > 100)
            {
                return SbiErrorHelper.TechnicalErrorDuringExtraction;
            }

            requestedScore = (int)Math.Round(score, MidpointRounding.AwayFromZero);
        }

        if (string.IsNullOrEmpty(bio.PreviousHash))
        {
            return null;
        }

        if (bio.PreviousHash.Length != 64)
        {
            return SbiErrorHelper.TechnicalErrorDuringExtraction;
        }

        try
        {
            previousHash = Convert.FromHexString(bio.PreviousHash);
        }
        catch (FormatException)
        {
            return SbiErrorHelper.TechnicalErrorDuringExtraction;
        }

        return null;
    }

    /// <summary>
    /// 将 SBI 环境字符串转换为 FTM 环境
    /// </summary>
    private static bool TryMapEnvironment(string? value, out FtmEnvironment environment)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
            case "staging":
                environment = FtmEnvironment.Staging;
                return true;
            case "developer":
                environment = FtmEnvironment.Developer;
                return true;
            case "pre-production":
                environment = FtmEnvironment.PreProduction;
                return true;
            case "production":
                environment = FtmEnvironment.Production;
                return true;
            default:
                environment = default;
                return false;
        }
    }

    /// <summary>
    /// 校验事务 ID：长度 4..50，只允许字母、数字与连字符
    /// </summary>
    private static bool IsValidTransactionId(string? transactionId)
    {
        if (string.IsNullOrEmpty(transactionId) || transactionId.Length is < 4 or > 50)
        {
            return false;
        }

        return transactionId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    /// <summary>
    /// 取采集超时；未指定或非正数时使用 FAP20 默认值
    /// </summary>
    private static uint ResolveTimeout(int? timeoutMs) =>
        timeoutMs is > 0 ? (uint)timeoutMs.Value : FAP20.DefaultCaptureTimeoutMs;

    /// <summary>
    /// 取采集时刻；缺失或无法解析时使用当前 UTC
    /// </summary>
    private static DateTimeOffset ResolveCaptureTime(string? captureTime) =>
        DateTimeOffset.TryParse(captureTime, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UtcNow;

    /// <summary>
    /// 解析 FTM 采集响应；兼容单条生物特征对象、<c>biometrics</c> 数组和直接数组三种形态
    /// </summary>
    private static BiometricInfo ParseBiometric(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return CreateBiometricError(SbiErrorHelper.UnableToDetectBiometricObject);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response);
        }
        catch (JsonException exception)
        {
            Logger.Error(exception, "认证采集响应不是合法 JSON");
            return CreateBiometricError(SbiErrorHelper.TechnicalErrorDuringExtraction);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return ParseFirstBiometric(root) ??
                       CreateBiometricError(SbiErrorHelper.UnableToDetectBiometricObject);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return CreateBiometricError(SbiErrorHelper.TechnicalErrorDuringExtraction);
            }

            if (TryGetProperty(root, "biometrics", out var biometrics)
                && biometrics.ValueKind == JsonValueKind.Array)
            {
                return ParseFirstBiometric(biometrics) ??
                       CreateBiometricError(SbiErrorHelper.UnableToDetectBiometricObject);
            }

            return DeserializeBiometric(root);
        }
    }

    /// <summary>
    /// 取数组中的第一条生物特征对象
    /// </summary>
    private static BiometricInfo? ParseFirstBiometric(JsonElement array)
    {
        var item = array.EnumerateArray()
            .FirstOrDefault(element => element.ValueKind == JsonValueKind.Object);

        return item.ValueKind == JsonValueKind.Object ? DeserializeBiometric(item) : null;
    }

    /// <summary>
    /// 反序列化并补齐生物特征条目的默认字段
    /// </summary>
    private static BiometricInfo DeserializeBiometric(JsonElement element)
    {
        return NormalizeBiometric(new BiometricInfo
        {
            SpecVersion = ReadString(element, "specVersion"),
            Data = ReadString(element, "data"),
            Hash = ReadString(element, "hash"),
            SessionKey = ReadString(element, "sessionKey"),
            Thumbprint = ReadString(element, "thumbprint"),
            Error = ReadError(element)
        });
    }

    /// <summary>
    /// 补齐规范版本与错误对象；不得改写签名/加密内容
    /// </summary>
    private static BiometricInfo NormalizeBiometric(BiometricInfo biometric)
    {
        biometric.SpecVersion ??= DefaultSpecVersion;

        if (string.IsNullOrEmpty(biometric.Data))
        {
            if (biometric.Error is null
                || string.IsNullOrEmpty(biometric.Error.ErrorCode)
                || biometric.Error.ErrorCode == SuccessErrorCode)
            {
                biometric.Error = SbiErrorHelper.UnableToDetectBiometricObject;
            }
        }
        else
        {
            biometric.Error ??= SbiErrorHelper.Success;
        }

        return biometric;
    }

    /// <summary>
    /// 构造一条生物特征错误记录
    /// </summary>
    private static BiometricInfo CreateBiometricError(SbiError error) => new() { Error = error };

    /// <summary>
    /// 为每个请求项构造一条错误记录
    /// </summary>
    private static CaptureResponse CreateErrorResponse(int count, Func<SbiError> errorFactory)
    {
        var itemCount = Math.Max(1, count);
        var biometrics = new List<BiometricInfo>(itemCount);

        for (var i = 0; i < itemCount; i++)
        {
            biometrics.Add(CreateBiometricError(errorFactory()));
        }

        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 按名称大小写不敏感地读取 JSON 属性
    /// </summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 读取字符串属性
    /// </summary>
    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 读取字符串或数字属性（错误码可能是裸数字）
    /// </summary>
    private static string? ReadVerbatim(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    /// <summary>
    /// 读取 <c>error</c> 对象
    /// </summary>
    private static SbiError? ReadError(JsonElement element)
    {
        if (!TryGetProperty(element, "error", out var error)
            || error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new SbiError
        {
            ErrorCode = ReadVerbatim(error, "errorCode"),
            ErrorInfo = ReadString(error, "errorInfo")
        };
    }
}
