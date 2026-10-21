using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Device.Fcg3M115;
using SbiAuthenticationBridge.Device.Protocol;
using Serilog;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// FAP20 的 SBI 认证设备句柄：设备发现、设备信息与指纹采集
/// </summary>
/// <remarks>
/// <para>
/// 配置了 FCG-3M115 Host 私钥且固件支持新协议时，使用 336 字节签名请求采集原图；
/// 否则回退到旧版 <c>CAPTURE_AUTH</c> 紧凑二进制协议，由设备直接返回 SBI 生物特征 JSON。
/// </para>
/// <para>
/// 固件是否支持 FCG-3M115 可通过 <c>ftm_capture_auth</c> 的响应格式确认：
/// 新固件返回 <c>FCP1</c> 二进制，旧固件返回 JSON。
/// </para>
/// </remarks>
public sealed class Fap20AuthenticationDevice : Fap20SbiDevice, IAuthenticationDevice
{
    private const string SuccessErrorCode = "0";

    private const string DefaultSpecVersion = "0.9.5";

    /// <summary>
    /// Host 证书上传超时：新 SDK 会分段发送证书
    /// </summary>
    private const uint HostCertificateTimeoutMs = 30_000;

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
    /// 认证指纹采集
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

        var deviceCode = await EnsureDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(deviceCode))
        {
            return CreateErrorResponse(bios.Count, () => SbiErrorHelper.DeviceNotReady);
        }

        // 只有配置了 Host 私钥才尝试 FCG-3M115；当前已确认旧固件不支持该协议，
        // 未配置密钥时应直接走旧的 CAPTURE_AUTH，而不是在配置阶段报 102。
        if (Fcg3M115Configuration.TryGetHostKey(out var hostKey))
        {
            Logger.Information("使用 FCG-3M115 安全采图协议");
            return await CaptureFcg3M115Async(request, bios, deviceCode, hostKey,
                cancellationToken).ConfigureAwait(false);
        }

        Logger.Information("未配置 FCG-3M115 Host 私钥，使用旧版 CAPTURE_AUTH 协议");
        return await CaptureLegacyAsync(request, bios, deviceCode, cancellationToken)
            .ConfigureAwait(false);
    }

    #region FCG-3M115

    /// <summary>
    /// FCG-3M115 安全采图：上传 Host 证书、发送签名请求、验签解密并组装 SBI 响应
    /// </summary>
    private async Task<CaptureResponse> CaptureFcg3M115Async(CaptureRequest request,
        IReadOnlyList<BioInfo> bios, string deviceCode, IFcg3M115HostKey hostKey,
        CancellationToken cancellationToken)
    {
        X509Certificate2 rootCertificate;
        X509Certificate2 applicationCertificate;

        try
        {
            rootCertificate = Fcg3M115Configuration.GetRootCertificate();
            applicationCertificate = Fcg3M115Configuration.GetApplicationCertificate();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "FCG-3M115 配置不完整，无法采图");

            var timeoutMs = ResolveTimeout(request.Timeout);
            var probe = await ProbeCaptureProtocolAsync(timeoutMs, cancellationToken)
                .ConfigureAwait(false);

            var detail = probe is null
                ? $"{exception.GetType().Name}: {exception.Message}"
                : $"{exception.GetType().Name}: {exception.Message}；协议探测：{probe}";

            return CreateErrorResponse(bios.Count, () => TechnicalError(detail));
        }

        var captureTimeoutMs = ResolveTimeout(request.Timeout);
        var biometrics = new List<BiometricInfo>(bios.Count);

        foreach (var bio in bios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            biometrics.Add(await CaptureFcg3M115OneAsync(hostKey, rootCertificate,
                applicationCertificate, deviceCode, request, bio, captureTimeoutMs,
                cancellationToken).ConfigureAwait(false));
        }

        Logger.Information("认证采集完成，返回 {Count} 条结果", biometrics.Count);
        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 采集单个 bio 项（FCG-3M115）
    /// </summary>
    private async Task<BiometricInfo> CaptureFcg3M115OneAsync(IFcg3M115HostKey hostKey,
        X509Certificate2 rootCertificate, X509Certificate2 applicationCertificate,
        string deviceCode, CaptureRequest request, BioInfo bio, uint timeoutMs,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateFcg3M115Bio(bio, out var bioSubType);
        if (validationError is not null)
        {
            return new BiometricInfo { Error = validationError };
        }

        try
        {
            await Device.EnsureHostCertificateAsync(hostKey.CertificateDer,
                HostCertificateTimeoutMs, cancellationToken).ConfigureAwait(false);

            var secureRequest = Fcg3M115Protocol.CreateRequest(hostKey);
            var response = await Device.CaptureSecureImageAsync(secureRequest.Bytes, timeoutMs,
                cancellationToken).ConfigureAwait(false);

            var image = Fcg3M115Protocol.VerifyAndDecrypt(response, hostKey, rootCertificate,
                secureRequest);

            return SbiCaptureResponseBuilder.Create(hostKey, applicationCertificate, deviceCode,
                bioSubType, request, bio, image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Fcg3M115Exception exception)
        {
            Logger.Error(exception, "FCG-3M115 响应校验失败");
            return new BiometricInfo { Error = TechnicalError(exception) };
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "认证采集失败");
            return new BiometricInfo
            {
                Error = Device.IsConnected
                    ? TechnicalError(exception)
                    : SbiErrorHelper.DeviceNotReady
            };
        }
    }

    /// <summary>
    /// 下发内置 Host 证书并调用 <c>ftm_capture_auth</c>，确认固件实际返回的采集协议
    /// </summary>
    /// <remarks>
    /// 探测请求没有有效 Host 签名，模组会拒绝采集；这里只关心响应格式。
    /// 旧固件返回 JSON 文本，FCG-3M115 固件返回 124104 字节 FCP1 响应。
    /// </remarks>
    /// <returns>设备响应摘要；探测本身失败时返回 <c>null</c></returns>
    private async Task<string?> ProbeCaptureProtocolAsync(uint timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var hostCertificateDer = Fcg3M115Protocol.LoadEmbeddedHostCertificateDer();

            try
            {
                await Device.EnsureHostCertificateAsync(hostCertificateDer,
                    HostCertificateTimeoutMs, cancellationToken).ConfigureAwait(false);
                Logger.Information("协议探测：Host 证书已下发");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception,
                    "协议探测：Host 证书下发失败，继续调用 ftm_capture_auth 确认固件响应");
            }

            var probeRequest = Fcg3M115Protocol.CreateProbeRequest(hostCertificateDer);
            var probe = await Device.ProbeCaptureAuthAsync(probeRequest.Bytes, timeoutMs,
                cancellationToken).ConfigureAwait(false);

            Logger.Information(
                "协议探测：固件返回 {Protocol}，原生返回码 {Result}（0x{ResultHex}）",
                probe.IsFcg3M115 ? "FCG-3M115 FCP1 响应" : "非 FCP1 响应",
                probe.NativeResult, probe.NativeResult);

            return probe.Describe();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "协议探测失败");
            return null;
        }
    }

    /// <summary>
    /// 校验并提取单个 bio 项的采集参数（FCG-3M115）
    /// </summary>
    private static SbiError? ValidateFcg3M115Bio(BioInfo bio, out string? bioSubType)
    {
        bioSubType = "UNKNOWN";

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
            if (string.IsNullOrWhiteSpace(subTypes[0]))
            {
                return SbiErrorHelper.TechnicalErrorDuringExtraction;
            }

            bioSubType = subTypes[0];
        }

        if (bio.RequestedScore is { } score && (double.IsNaN(score) || score is < 0 or > 100))
        {
            return SbiErrorHelper.TechnicalErrorDuringExtraction;
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
            _ = Convert.FromHexString(bio.PreviousHash);
        }
        catch (FormatException)
        {
            return SbiErrorHelper.TechnicalErrorDuringExtraction;
        }

        return null;
    }

    /// <summary>
    /// 构造技术错误；Debug 构建附带具体原因，Release 使用标准描述
    /// </summary>
    private static SbiError TechnicalError(string detail)
    {
#if DEBUG
        return SbiErrorHelper.TechnicalErrorDetail(detail);
#else
        _ = detail;
        return SbiErrorHelper.TechnicalErrorDuringExtraction;
#endif
    }

    /// <summary>
    /// 构造技术错误；Debug 构建附带具体原因，Release 使用标准描述
    /// </summary>
    private static SbiError TechnicalError(Exception exception)
    {
#if DEBUG
        return SbiErrorHelper.TechnicalErrorDetail(
            $"{exception.GetType().Name}: {exception.Message}");
#else
        _ = exception;
        return SbiErrorHelper.TechnicalErrorDuringExtraction;
#endif
    }

    #endregion

    #region Legacy CAPTURE_AUTH

    /// <summary>
    /// 旧版紧凑二进制采集：设备直接返回 SBI 生物特征 JSON
    /// </summary>
    private async Task<CaptureResponse> CaptureLegacyAsync(CaptureRequest request,
        IReadOnlyList<BioInfo> bios, string deviceCode, CancellationToken cancellationToken)
    {
        if (!TryMapEnvironment(request.Env, out var environment))
        {
            return CreateErrorResponse(bios.Count,
                () => SbiErrorHelper.TechnicalErrorDuringExtraction);
        }

        // 旧固件用 SERVER_ENCRYPTION 证书加密 SBI 响应中的会话密钥；
        // 证书在设备 RAM 中，连接/复位后需要重新加载，否则设备返回 108。
        var encryptionCertificate = TryGetApplicationCertificateDer();
        if (encryptionCertificate is not null)
        {
            try
            {
                await Device.EnsureEncryptionCertificateAsync(encryptionCertificate,
                    FAP20.DefaultCertificateTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Error(exception, "旧版采图前加载 SERVER_ENCRYPTION 证书失败");
            }
        }

        var transactionId = request.TransactionId!;
        var timeoutMs = ResolveTimeout(request.Timeout);
        var captureTime = ResolveCaptureTime(request.CaptureTime);
        var biometrics = new List<BiometricInfo>(bios.Count);

        foreach (var bio in bios)
        {
            var biometric = await CaptureLegacyOneAsync(transactionId, request.DomainUri, bio,
                deviceCode, environment, captureTime, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            biometrics.Add(biometric);
        }

        Logger.Information("认证采集完成，返回 {Count} 条结果", biometrics.Count);
        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 采集单个 bio 项（旧版协议）
    /// </summary>
    private async Task<BiometricInfo> CaptureLegacyOneAsync(string transactionId, string? domainUri,
        BioInfo bio, string deviceCode, FtmEnvironment environment, DateTimeOffset captureTime,
        uint timeoutMs, CancellationToken cancellationToken)
    {
        var validationError = ValidateLegacyBio(bio, out var fingerPosition, out var requestedScore,
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
    /// 校验并转换单个 bio 项的采集参数（旧版协议）
    /// </summary>
    private static SbiError? ValidateLegacyBio(BioInfo bio, out FtmFingerPosition fingerPosition,
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
    /// 读取 SBI 应用/服务器加密证书的 DER；未配置时返回 <c>null</c>
    /// </summary>
    /// <remarks>
    /// 旧固件和 FCG-3M115 共用同一个 SBI 应用公钥证书：
    /// Android 为 <c>FilesDir/fcg3m115_app.cert.der</c>，桌面为 <c>FCG3M115_APP_CERT</c>。
    /// </remarks>
    private static byte[]? TryGetApplicationCertificateDer()
    {
        try
        {
            return Fcg3M115Configuration.GetApplicationCertificate().RawData;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception,
                "未配置 SBI 应用/服务器加密证书，旧固件采图可能返回 108 Missing certificate or key material");
            return null;
        }
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

        if (!string.IsNullOrEmpty(biometric.Data))
        {
            biometric.DataDecoded = SbiDataDecoder.TryDecode(biometric.Data);
        }

        return biometric;
    }

    /// <summary>
    /// 构造一条生物特征错误记录
    /// </summary>
    private static BiometricInfo CreateBiometricError(SbiError error) => new() { Error = error };

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

    #endregion

    #region Shared

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
    /// 为每个请求项构造一条错误记录
    /// </summary>
    private static CaptureResponse CreateErrorResponse(int count, Func<SbiError> errorFactory)
    {
        var itemCount = Math.Max(1, count);
        var biometrics = new List<BiometricInfo>(itemCount);

        for (var index = 0; index < itemCount; index++)
        {
            biometrics.Add(CreateBiometricError(errorFactory()));
        }

        return new CaptureResponse { Biometrics = biometrics };
    }

    #endregion
}
