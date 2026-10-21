using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using Serilog;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// 模拟 FAP20：不访问 USB/原生库，直接返回符合 SBI 报文结构的模拟数据
/// </summary>
/// <remarks>
/// <para>
/// 用于在没有指纹硬件、没有 Android 版 <c>libftmBridgeAPI.so</c> 的环境中测试
/// Intent 宿主、服务编排与界面。Debug 构建默认使用本设备，Release 构建使用真实 FAP20。
/// </para>
/// <para>
/// 模拟数据只保证字段结构与取值格式正确；采集 JWT 由本机生成的设备密钥签名，
/// 会话密钥与证书摘要仍为占位值，不能用于真实认证。
/// </para>
/// </remarks>
public sealed class SimulatedFap20Device : IAuthenticationDevice, IDevice, IDeviceKeyRotation
{
    private const string SpecVersion = "0.9.5";

    private const string DeviceCode = "MOCK00000001";

    private const string DeviceId = "MOCK-FAP20-0001";

    private const string Manufacturer = "Simulated";

    private const string Model = "FAP20-Mock";

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "MockFAP20");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _callbackId;

    /// <summary>
    /// 保护模拟设备密钥与证书；签名与轮换都在此锁内完成
    /// </summary>
    private readonly Lock _keyGate = new();

    private volatile bool _connected;

    private volatile DeviceStatus _status = DeviceStatus.Ready;

    private RSA? _deviceKey;

    private X509Certificate2? _deviceCertificate;

    private int _rotationId = 1;

    private bool _disposed;

    /// <summary>
    /// 创建模拟 FAP20
    /// </summary>
    /// <param name="callbackId">发现/信息响应中的回调地址，Android 上应为 Intent appId</param>
    public SimulatedFap20Device(string callbackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackId);
        _callbackId = callbackId;
    }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public Task ConnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _connected = true;
        Logger.Information("模拟 FAP20 已连接");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        _connected = false;
        Logger.Information("模拟 FAP20 已断开");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _status = status;
        Logger.Information("模拟 FAP20 状态设置为 {Status}", status);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_keyGate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _connected = false;
            _deviceKey?.Dispose();
            _deviceCertificate?.Dispose();
            _deviceKey = null;
            _deviceCertificate = null;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!CanAnswerDiscovery(request.Type))
        {
            return Task.FromResult<IReadOnlyList<DiscoveryResponse>>([]);
        }

        var response = new DiscoveryResponse
        {
            DeviceId = DeviceId,
            DeviceStatus = GetStatusText(),
            Certification = "L0",
            ServiceVersion = SpecVersion,
            DeviceSubId = [1],
            CallbackId = _callbackId,
            DigitalId = CreateDigitalId(),
            DeviceCode = DeviceCode,
            SpecVersion = [SpecVersion],
            Purpose = "Auth",
            Error = CreateStatusError()
        };

        Logger.Information("模拟 FAP20 应答设备发现，状态 {Status}", _status);
        return Task.FromResult<IReadOnlyList<DiscoveryResponse>>([response]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var info = new DeviceInfo
        {
            DeviceStatus = GetStatusText(),
            DeviceId = DeviceId,
            Firmware = "SIM-1.0",
            Certification = "L0",
            ServiceVersion = SpecVersion,
            DeviceSubId = [1],
            CallbackId = _callbackId,
            DigitalId = CreateSignedDigitalId(),
            DeviceCode = DeviceCode,
            Env = "Staging",
            Purpose = "Auth",
            SpecVersion = [SpecVersion]
        };

        // 已注册设备的 deviceInfo 必须由设备密钥签名，CTK 从中读取 x5c 跟踪密钥轮换
        var encoded = CreateSignedJwt(JsonSerializer.SerializeToUtf8Bytes(info, JsonOptions));

        Logger.Information("模拟 FAP20 应答设备信息，状态 {Status}", _status);
        return Task.FromResult<IReadOnlyList<DeviceInfoResponse>>(
            [new DeviceInfoResponse { DeviceInfo = encoded }]);
    }

    /// <inheritdoc />
    public async Task<CaptureResponse> CaptureAsync(CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bios = request.Bio;
        if (bios is null || bios.Count == 0)
        {
            return CreateErrorResponse(1, static () => SbiErrorHelper.RequestedBiometricCountNotSupported);
        }

        if (!IsValidTransactionId(request.TransactionId))
        {
            return CreateErrorResponse(bios.Count, static () => SbiErrorHelper.InvalidTransactionId);
        }

        if (_status != DeviceStatus.Ready)
        {
            return CreateErrorResponse(bios.Count, CreateStatusError);
        }

        Logger.Information("模拟 FAP20 开始采集，事务 {TransactionId:l}", request.TransactionId);
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        var biometrics = new List<BiometricInfo>(bios.Count);

        foreach (var bio in bios)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReferenceEquals(bio, null))
            {
                biometrics.Add(new BiometricInfo
                {
                    Error = SbiErrorHelper.TechnicalErrorDuringExtraction
                });
                continue;
            }

            biometrics.Add(CreateCapture(bio, request));
        }

        Logger.Information("模拟 FAP20 采集完成，返回 {Count} 条结果", biometrics.Count);
        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 构造单个 bio 项的模拟采集结果
    /// </summary>
    private BiometricInfo CreateCapture(BioInfo bio, CaptureRequest request)
    {
        if (!string.IsNullOrEmpty(bio.Type)
            && !string.Equals(bio.Type, "Finger", StringComparison.OrdinalIgnoreCase))
        {
            return new BiometricInfo { Error = SbiErrorHelper.DeviceNotFound };
        }

        var count = bio.Count ?? 1;
        if (count != 1 || bio.BioSubType is { Count: > 1 })
        {
            return new BiometricInfo { Error = SbiErrorHelper.RequestedBiometricCountNotSupported };
        }

        var transactionId = request.TransactionId!;
        var bioSubType = bio.BioSubType is { Count: 1 } subTypes ? subTypes[0] : "UNKNOWN";
        var timestamp = DateTimeOffset.UtcNow;

        // 模拟 ISO 模板；真实设备这里会是 ISO/IEC 19794-4 数据
        var isoData = Encoding.UTF8.GetBytes(
            $"MOCK-ISO|{transactionId}|{bioSubType}|{timestamp:O}");

        var captureData = new CaptureData
        {
            DigitalId = CreateSignedDigitalId(),
            DeviceCode = DeviceCode,
            DeviceServiceVersion = SpecVersion,
            BioType = "Finger",
            BioSubType = bioSubType,
            Purpose = request.Purpose ?? "Auth",
            Env = request.Env ?? "Staging",
            DomainUri = request.DomainUri,
            BioValue = Base64UrlEncode(isoData),
            TransactionId = transactionId,
            Timestamp = timestamp.ToString("O"),
            RequestedScore = bio.RequestedScore,
            QualityScore = 85
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(captureData, JsonOptions);

        return new BiometricInfo
        {
            SpecVersion = SpecVersion,
            Data = CreateSignedJwt(payload),
            Hash = ComputeHash(bio.PreviousHash, isoData),
            SessionKey = Base64UrlEncode(SHA256.HashData("mock-session-key"u8)),
            Thumbprint = Convert.ToHexString(SHA256.HashData("mock-certificate"u8)),
            Error = SbiErrorHelper.Success
        };
    }

    /// <summary>
    /// 构造模拟的设备数字身份（未签名 base64url payload，用于设备发现）
    /// </summary>
    private static string CreateDigitalId() =>
        Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(CreateDigitalIdValue(), JsonOptions));

    /// <summary>
    /// 构建设备密钥签名的数字身份（用于设备信息与采集数据）
    /// </summary>
    private string CreateSignedDigitalId() =>
        CreateSignedJwt(JsonSerializer.SerializeToUtf8Bytes(CreateDigitalIdValue(), JsonOptions));

    /// <summary>
    /// 构建设备数字身份内容
    /// </summary>
    private static DigitalId CreateDigitalIdValue() => new()
    {
        SerialNo = DeviceCode,
        Make = Manufacturer,
        Model = Model,
        Type = "Finger",
        DeviceSubType = "Single",
        DeviceProvider = Manufacturer,
        DeviceProviderId = "SIM",
        DateTime = DateTimeOffset.UtcNow.ToString("O")
    };

    /// <summary>
    /// 使用当前设备密钥签名采集数据，并在 JWT 头中携带证书（x5c）
    /// </summary>
    /// <param name="payload">JWT 载荷</param>
    /// <returns>JWT</returns>
    private string CreateSignedJwt(byte[] payload)
    {
        lock (_keyGate)
        {
            var certificate = EnsureDeviceCertificateLocked();
            var certificateDer = certificate.Export(X509ContentType.Cert);

            var header = new
            {
                alg = "RS256",
                typ = "JWT",
                x5c = new[] { Convert.ToBase64String(certificateDer) }
            };

            var headerPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
            var payloadPart = Base64UrlEncode(payload);
            var signingInput = Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}");
            var signature = _deviceKey!.SignData(signingInput, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return $"{headerPart}.{payloadPart}.{Base64UrlEncode(signature)}";
        }
    }

    #region IDeviceKeyRotation

    /// <inheritdoc />
    public bool RequiresCertificate => false;

    /// <inheritdoc />
    public Task<DeviceKeyInfo> GetKeyInfoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_keyGate)
        {
            var info = CreateDeviceKeyInfoLocked(EnsureDeviceCertificateLocked());
            return Task.FromResult(info);
        }
    }

    /// <inheritdoc />
    /// <remarks>模拟设备始终在本机生成新的 RSA 密钥与自签证书，忽略外部证书。</remarks>
    public Task<DeviceKeyInfo> RotateKeyAsync(ReadOnlyMemory<byte>? certificateDer = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // 模拟设备没有可安装证书的硬件密钥区，始终生成本机新密钥
        _ = certificateDer;

        lock (_keyGate)
        {
            _deviceKey?.Dispose();
            _deviceCertificate?.Dispose();
            _deviceKey = null;
            _deviceCertificate = null;
            _rotationId++;

            var info = CreateDeviceKeyInfoLocked(EnsureDeviceCertificateLocked());
            Logger.Information("模拟 FAP20 密钥已更换，代数 {RotationId}，指纹 {Fingerprint:l}",
                info.RotationId, info.Fingerprint);

            return Task.FromResult(info);
        }
    }

    /// <summary>
    /// 生成新的 RSA 设备密钥与自签证书；调用方需持有 <see cref="_keyGate"/>
    /// </summary>
    private X509Certificate2 EnsureDeviceCertificateLocked()
    {
        if (_deviceCertificate is not null)
        {
            return _deviceCertificate;
        }

        var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={DeviceCode}", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey,
            false));

        _deviceCertificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        _deviceKey = key;

        Logger.Information("模拟 FAP20 已生成设备密钥，代数 {RotationId}", _rotationId);
        return _deviceCertificate;
    }

    /// <summary>
    /// 构造设备密钥信息；调用方需持有 <see cref="_keyGate"/>
    /// </summary>
    private DeviceKeyInfo CreateDeviceKeyInfoLocked(X509Certificate2 certificate) => new()
    {
        RotationId = _rotationId,
        SubjectPublicKeyInfo = _deviceKey!.ExportSubjectPublicKeyInfo(),
        Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.Export(X509ContentType.Cert)))
    };

    #endregion

    /// <summary>
    /// 按“上一个哈希 + 本次 ISO 数据”的 SHA-256 规则计算哈希链值
    /// </summary>
    private static string ComputeHash(string? previousHash, byte[] isoData)
    {
        var previous = TryParseHex(previousHash);

        var buffer = new byte[previous.Length + isoData.Length];
        previous.CopyTo(buffer, 0);
        isoData.CopyTo(buffer, previous.Length);

        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    private static byte[] TryParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return [];
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static bool IsValidTransactionId(string? transactionId)
    {
        if (transactionId is null || transactionId.Length is < 4 or > 50)
        {
            return false;
        }

        foreach (var character in transactionId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAnswerDiscovery(string? type)
    {
        return string.IsNullOrWhiteSpace(type)
               || string.Equals(type, "Finger", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "Biometric Device", StringComparison.OrdinalIgnoreCase);
    }

    private string GetStatusText() => _status switch
    {
        DeviceStatus.Ready => "Ready",
        DeviceStatus.Busy => "Busy",
        DeviceStatus.NotReady => "Not Ready",
        DeviceStatus.NotRegistered => "Not Registered",
        _ => "Not Ready"
    };

    private SbiError CreateStatusError() => _status switch
    {
        DeviceStatus.Busy => SbiErrorHelper.DeviceBusy,
        DeviceStatus.NotReady => SbiErrorHelper.DeviceNotReady,
        DeviceStatus.NotRegistered => SbiErrorHelper.DeviceNotRegistered,
        _ => SbiErrorHelper.Success
    };

    private static CaptureResponse CreateErrorResponse(int count, Func<SbiError> errorFactory)
    {
        var biometrics = new List<BiometricInfo>(count);

        for (var index = 0; index < count; index++)
        {
            biometrics.Add(new BiometricInfo { Error = errorFactory() });
        }

        return new CaptureResponse { Biometrics = biometrics };
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
