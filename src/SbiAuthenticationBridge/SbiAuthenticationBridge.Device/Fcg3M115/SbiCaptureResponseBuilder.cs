using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// 把 FCG-3M115 解密出的原图组装成 SBI 采集响应
/// </summary>
/// <remarks>
/// <para>
/// 新固件只返回 300×400 GRAY8 原图，不再生成 MOSIP JWT。这里以 Host 证书私钥签名
/// <c>data</c> JWT，并把原图作为 <c>bioValue</c>；<c>sessionKey</c> 使用配置的 SBI 应用
/// 公钥证书按 RSA-OAEP-SHA256 包装，<c>thumbprint</c> 为该证书的 SHA-256。
/// </para>
/// <para>
/// 原图不是 ISO/IEC 19794-4 模板，因此 CTK 的 ISO 用例会失败；如需通过 ISO 校验，
/// 需要在调用前把原图转换成 ISO 模板。
/// </para>
/// </remarks>
public static class SbiCaptureResponseBuilder
{
    private const string DefaultSpecVersion = "0.9.5";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 构造一条 SBI 采集结果
    /// </summary>
    /// <param name="hostKey">Host 密钥，用于签名 JWT</param>
    /// <param name="applicationCertificate">SBI 应用公钥证书，用于包装会话密钥</param>
    /// <param name="deviceCode">设备序列号</param>
    /// <param name="bioSubType">本次采集部位</param>
    /// <param name="request">原始采集请求</param>
    /// <param name="bio">当前 bio 项</param>
    /// <param name="rawImage">解密后的 GRAY8 原图</param>
    /// <returns>SBI 采集结果</returns>
    public static BiometricInfo Create(IFcg3M115HostKey hostKey,
        X509Certificate2 applicationCertificate, string deviceCode, string? bioSubType,
        CaptureRequest request, BioInfo bio, ReadOnlyMemory<byte> rawImage)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        ArgumentNullException.ThrowIfNull(applicationCertificate);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bio);

        if (rawImage.IsEmpty)
        {
            throw new Fcg3M115Exception("解密后的原图为空。");
        }

        var specVersion = string.IsNullOrWhiteSpace(request.SpecVersion)
            ? DefaultSpecVersion
            : request.SpecVersion;

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);

        var transactionId = request.TransactionId!;

        var (bioValue, sessionKey, thumbprint) = EncryptImage(applicationCertificate,
            rawImage.Span, timestamp, transactionId);

        var data = new CaptureData
        {
            DigitalId = CreateDigitalId(hostKey, deviceCode, timestamp),
            DeviceCode = deviceCode,
            DeviceServiceVersion = specVersion,
            BioType = "Finger",
            BioSubType = bioSubType ?? "UNKNOWN",
            Purpose = request.Purpose ?? "Auth",
            Env = request.Env ?? "Staging",
            DomainUri = request.DomainUri,
            BioValue = bioValue,
            TransactionId = transactionId,
            Timestamp = timestamp,
            RequestedScore = bio.RequestedScore,
            // 新固件不再计算 NFIQ；这里填 0 占位，接入 NFIQ 后应改为真实质量分
            QualityScore = 0
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        var dataJwt = CreateJwt(hostKey, payload);

        return new BiometricInfo
        {
            SpecVersion = specVersion,
            Data = dataJwt,
            DataDecoded = SbiDataDecoder.TryDecode(dataJwt),
            Hash = ComputeHashChain(bio.PreviousHash, rawImage.Span),
            SessionKey = sessionKey,
            Thumbprint = thumbprint,
            Error = SbiErrorHelper.Success
        };
    }

    /// <summary>
    /// 使用 Host 密钥签名 JWT，并在头部携带 Host 证书（x5c）
    /// </summary>
    private static string CreateJwt(IFcg3M115HostKey hostKey, byte[] payload)
    {
        var header = new
        {
            alg = "RS256",
            typ = "JWT",
            x5c = new[] { Convert.ToBase64String(hostKey.CertificateDer.Span) }
        };

        var headerPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadPart = Base64UrlEncode(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}");
        var signature = hostKey.Sign(signingInput);

        return $"{headerPart}.{payloadPart}.{Base64UrlEncode(signature)}";
    }

    /// <summary>
    /// 构造并签名 digitalId JWT
    /// </summary>
    private static string CreateDigitalId(IFcg3M115HostKey hostKey, string deviceCode,
        string timestamp)
    {
        var digitalId = new DigitalId
        {
            SerialNo = deviceCode,
            Make = "ICT",
            Model = "FAP20",
            Type = "Finger",
            DeviceSubType = "Single",
            DeviceProvider = "ICT",
            DeviceProviderId = "ICT",
            DateTime = timestamp
        };

        return CreateJwt(hostKey,
            JsonSerializer.SerializeToUtf8Bytes(digitalId, JsonOptions));
    }

    /// <summary>
    /// 按 SBI 规则计算哈希链：SHA256(previousHash || 本次生物特征数据)，大写十六进制
    /// </summary>
    private static string ComputeHashChain(string? previousHash, ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> previous = string.IsNullOrWhiteSpace(previousHash)
            ? SHA256.HashData([])
            : ParsePreviousHash(previousHash);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(previous);
        hash.AppendData(image);

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] ParsePreviousHash(string previousHash)
    {
        if (previousHash.Length != 64)
        {
            throw new Fcg3M115Exception("previousHash 必须是 64 个十六进制字符。");
        }

        try
        {
            return Convert.FromHexString(previousHash);
        }
        catch (FormatException exception)
        {
            throw new Fcg3M115Exception("previousHash 不是合法的十六进制字符串。", exception);
        }
    }

    /// <summary>
    /// AES-256-GCM 加密原图，并用应用公钥包装会话密钥
    /// </summary>
    private static (string BioValue, string SessionKey, string Thumbprint) EncryptImage(
        X509Certificate2 applicationCertificate, ReadOnlySpan<byte> image, string timestamp,
        string transactionId)
    {
        var (aad, iv) = DeriveAadAndIv(timestamp, transactionId);
        var aesKey = RandomNumberGenerator.GetBytes(32);

        try
        {
            var ciphertext = new byte[image.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(aesKey, 16))
            {
                aes.Encrypt(iv, image, ciphertext, tag, aad);
            }

            var encrypted = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(encrypted, 0);
            tag.CopyTo(encrypted, ciphertext.Length);

            using var applicationKey = applicationCertificate.GetRSAPublicKey()
                                       ?? throw new Fcg3M115Exception(
                                           "SBI 应用证书不含 RSA 公钥。");

            var wrappedKey = applicationKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
            var thumbprint = Convert.ToHexString(
                SHA256.HashData(applicationCertificate.Export(X509ContentType.Cert)));

            return (Base64UrlEncode(encrypted), Base64UrlEncode(wrappedKey), thumbprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    /// <summary>
    /// AAD = (timestamp XOR transactionId) 的后 16 字节，IV = 后 12 字节
    /// </summary>
    /// <remarks>
    /// 规范只给出了取后 16/12 字节的规则；这里按 timestamp 的 20 个 ASCII 字节逐字节异或，
    /// transactionId 不足 20 字节时循环复用。timestamp 固定为 <c>yyyy-MM-ddTHH:mm:ssZ</c>。
    /// </remarks>
    private static (byte[] Aad, byte[] Iv) DeriveAadAndIv(string timestamp, string transactionId)
    {
        var timestampBytes = Encoding.ASCII.GetBytes(timestamp);
        var transactionBytes = Encoding.ASCII.GetBytes(transactionId);

        if (transactionBytes.Length == 0 || timestampBytes.Length < 16)
        {
            throw new Fcg3M115Exception("无法由 timestamp 与 transactionId 派生 AAD/IV。");
        }

        var xor = new byte[timestampBytes.Length];

        for (var index = 0; index < xor.Length; index++)
        {
            xor[index] = (byte)(timestampBytes[index] ^ transactionBytes[index % transactionBytes.Length]);
        }

        return (xor[^16..], xor[^12..]);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
