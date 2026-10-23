using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Core.Model;
using SbiAuthenticationBridge.Device.Protocol;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// 把 FAP20 原生命令适配为 SBI 设备接口
/// </summary>
/// <remarks>
/// <para>
/// 本类只做协议映射与采集编排：不解析 <c>data</c> JWT 的载荷，也不重新签名，
/// 设备返回的签名字符串原样透传给上层。
/// </para>
/// <para>
/// 同一时刻只允许一个操作占用设备。并发请求不会排队，而是立即返回错误码
/// <c>111</c>（设备忙），与参考实现的 <c>DeviceMonitor.tryLock()</c> 语义一致。
/// </para>
/// </remarks>
// ReSharper disable once InconsistentNaming
public class FAP20Proxy : ISbiAuthenticationDevice, IDeviceProxy, IFtmSecureDevice, IManualDeviceStatus,
    IDevice
{
    #region Consts

    /// <summary>
    /// SBI 规范版本，采集响应中回填
    /// </summary>
    private const string SbiSpecVersion = "0.9.5";

    /// <summary>
    /// 采集请求未指定 timeout 时的默认等待时长
    /// </summary>
    private const int DefaultCaptureTimeoutMs = 30_000;

    /// <summary>
    /// 单次轮询调用原生命令的最长等待时间，便于在 SBI 超时前反复检查手指
    /// </summary>
    private const int CapturePollTimeoutMs = 3_000;

    /// <summary>
    /// 轮询间隔
    /// </summary>
    private const int CaptureRetryDelayMs = 200;

    /// <summary>
    /// 多指采集时留给用户换手指的时间
    /// </summary>
    private const int FingerChangeDelayMs = 1_000;

    /// <summary>
    /// requestedScore=100 的超时用例在 SBI 超时后继续采集的时长
    /// </summary>
    private const int TimeoutTestExtraMs = 10_000;

    /// <summary>
    /// 单次原生命令的最小超时，避免剩余时间过短导致调用立即失败
    /// </summary>
    private const int MinNativeTimeoutMs = 200;

    /// <summary>
    /// 单条采集请求允许的最大枚数；超过 9 时交给固件做数量不匹配校验
    /// </summary>
    private const int MaxBioCount = 255;

    /// <summary>
    /// deviceSubId 允许的最大值
    /// </summary>
    private const int MaxDeviceSubId = 3;

    /// <summary>
    /// 设备 ID 的最大长度
    /// </summary>
    private const int MaxDeviceIdLength = 20;

    /// <summary>
    /// 固件表示采集成功的错误码
    /// </summary>
    private const string SuccessErrorCode = "0";

    /// <summary>
    /// 固件表示尚未检测到手指的错误码，调用方应继续等待
    /// </summary>
    private const string NoFingerErrorCode = "101";

    /// <summary>
    /// 非法生物特征类型的供应商错误码，对应 SBI1196
    /// </summary>
    private const int InvalidBiometricTypeErrorCode = 504;

    private static readonly Dictionary<string, FtmFingerPosition> FingerPositions =
        new(StringComparer.Ordinal)
        {
            ["UNKNOWN"] = FtmFingerPosition.Unknown,
            ["Right Thumb"] = FtmFingerPosition.RightThumb,
            ["Right IndexFinger"] = FtmFingerPosition.RightIndexFinger,
            ["Right MiddleFinger"] = FtmFingerPosition.RightMiddleFinger,
            ["Right RingFinger"] = FtmFingerPosition.RightRingFinger,
            ["Right LittleFinger"] = FtmFingerPosition.RightLittleFinger,
            ["Left Thumb"] = FtmFingerPosition.LeftThumb,
            ["Left IndexFinger"] = FtmFingerPosition.LeftIndexFinger,
            ["Left MiddleFinger"] = FtmFingerPosition.LeftMiddleFinger,
            ["Left RingFinger"] = FtmFingerPosition.LeftRingFinger,
            ["Left LittleFinger"] = FtmFingerPosition.LeftLittleFinger
        };

    #endregion

    #region Fields

    private readonly FAP20 _fap20;

    /// <summary>
    /// 操作互斥；并发请求直接返回"设备忙"而不是排队
    /// </summary>
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>
    /// 从 DISCOVER/DEVICE_INFO 中缓存的设备序列号
    /// </summary>
    private volatile string? _cachedDeviceId;

    /// <summary>
    /// 最近一次设置或读取到的设备状态
    /// </summary>
    private volatile DeviceStatus _lastDeviceStatus = DeviceStatus.Ready;

    private volatile bool _disposed;

    /// <summary>
    /// 宿主上下文；回调标识在每次请求时读取，支持宿主运行时变更
    /// </summary>
    private readonly ISbiHostContext _hostContext;

    #endregion

    /// <summary>
    /// 创建 FAP20 的 SBI 适配器
    /// </summary>
    /// <param name="fap20">底层设备</param>
    /// <param name="hostContext">宿主上下文，提供当前回调标识</param>
    public FAP20Proxy(FAP20 fap20, ISbiHostContext hostContext)
    {
        ArgumentNullException.ThrowIfNull(fap20);
        ArgumentNullException.ThrowIfNull(hostContext);

        _fap20 = fap20;
        _hostContext = hostContext;
    }

    #region IDevice

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _fap20.ConnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _fap20.DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsConnected => !_disposed && _fap20.IsConnected;

    #endregion

    #region Properties

    /// <inheritdoc />
    public IDevice InnerDevice => _fap20;

    #endregion

    #region ISbiDevice

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await TryEnterOperationAsync(cancellationToken).ConfigureAwait(false))
        {
            return [new DiscoveryResponse { Error = SbiErrorHelper.DeviceBusy }];
        }

        try
        {
            var deviceType = ResolveDeviceType(request.Type, out var invalidType);
            var json = await _fap20.DiscoverAsync(_hostContext.CallbackId, deviceType,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var response = ParseDiscoveryResponse(json);

            CacheIdentity(response.DeviceCode, response.DigitalId);

            if (invalidType)
            {
                response.Error = SbiErrorHelper.Custom(InvalidBiometricTypeErrorCode,
                    $"Invalid biometric type: {request.Type}");
            }

            return [response];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDeviceFailure(exception))
        {
            Debug.WriteLine($"[FAP20Proxy] DISCOVER 失败：{exception}");
            return [new DiscoveryResponse { Error = MapDeviceFailure(exception) }];
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken).ConfigureAwait(false))
        {
            return [new DeviceInfoResponse { Error = SbiErrorHelper.DeviceBusy }];
        }

        try
        {
            var json = await _fap20.GetDeviceInfoAsync(_hostContext.CallbackId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var response = ParseDeviceInfoResponse(json);

            CacheIdentityFromDeviceInfo(response.DeviceInfo);

            return [response];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDeviceFailure(exception))
        {
            Debug.WriteLine($"[FAP20Proxy] DEVICE_INFO 失败：{exception}");
            return [new DeviceInfoResponse { Error = MapDeviceFailure(exception) }];
        }
        finally
        {
            _operationGate.Release();
        }
    }

    #endregion

    #region ISbiAuthenticationDevice

    /// <inheritdoc />
    public async Task<CaptureResponse> CaptureAsync(CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidTransactionId(request.TransactionId))
        {
            return CreateErrorResponse(SbiErrorHelper.InvalidTransactionId);
        }

        if (!await TryEnterOperationAsync(cancellationToken).ConfigureAwait(false))
        {
            return CreateErrorResponse(SbiErrorHelper.DeviceBusy);
        }

        try
        {
            return await CaptureCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<CaptureResponse> CaptureCoreAsync(CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var timeoutMs = request.Timeout is > 0 ? request.Timeout.Value : DefaultCaptureTimeoutMs;
        var environment = ResolveEnvironment(request.Env);
        var bioEntries = request.Bio is { Count: > 0 } ? request.Bio : null;

        var deviceId = await ResolveDeviceIdAsync(bioEntries, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Debug.WriteLine("[FAP20Proxy] 无法确定设备序列号，CAPTURE 中止。");
            return CreateErrorResponse(SbiErrorHelper.DeviceNotReady);
        }

        var previousHash = ResolvePreviousHash(bioEntries);
        var biometrics = new List<BiometricInfo>();
        var totalLoops = CountCaptureLoops(bioEntries);
        var captureIndex = 0;

        var entryCount = bioEntries?.Count ?? 1;
        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = bioEntries is null ? null : bioEntries[entryIndex];
            var captureCount = ResolveCaptureCount(entry);
            var loops = captureCount <= 9 ? captureCount : 1;

            for (var loop = 0; loop < loops; loop++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var captureRequest = new FtmCaptureRequest
                {
                    TransactionId = request.TransactionId!,
                    DeviceId = ResolveDeviceId(entry, deviceId),
                    Utc = DateTimeOffset.UtcNow,
                    FingerPosition = ResolveFingerPosition(entry, loop),
                    Environment = environment,
                    BioCount = captureCount,
                    RequestedScore = ResolveRequestedScore(entry),
                    DomainUri = request.DomainUri ?? "",
                    PreviousHash = previousHash,
                    DeviceSubId = Math.Clamp(entry?.DeviceSubId ?? 0, 0, MaxDeviceSubId)
                };

                BiometricInfo? result;

                if (captureCount > 9)
                {
                    // 枚数超过固件支持范围，只发一帧，让固件返回数量不匹配错误
                    result = await CaptureMismatchAsync(captureRequest, timeoutMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (captureRequest.RequestedScore == 100)
                {
                    result = await CaptureTimeoutTestAsync(captureRequest, timeoutMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = await CaptureNormalAsync(captureRequest, timeoutMs,
                        captureRequest.RequestedScore, cancellationToken).ConfigureAwait(false);
                }

                if (result is null)
                {
                    biometrics.Add(CreateErrorBiometric(SbiErrorHelper.TechnicalErrorDuringExtraction));
                    return new CaptureResponse { Biometrics = biometrics };
                }

                biometrics.Add(result);
                previousHash = ResolveHash(result.Hash) ?? previousHash;

                if (!IsSuccess(result))
                {
                    return new CaptureResponse { Biometrics = biometrics };
                }

                captureIndex++;
                if (captureIndex < totalLoops)
                {
                    await Task.Delay(FingerChangeDelayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 常规采集：在 SBI 超时前反复轮询，直到质量达标或固件返回错误
    /// </summary>
    private async Task<BiometricInfo?> CaptureNormalAsync(FtmCaptureRequest request, int timeoutMs,
        int requestedScore, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = deadline - Environment.TickCount64;
            var nativeTimeout = (uint)Math.Clamp(remaining, MinNativeTimeoutMs, CapturePollTimeoutMs);

            var biometric = await TryCaptureOnceAsync(request, nativeTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (biometric is null)
            {
                await Task.Delay(CaptureRetryDelayMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // 固件报错（除 101 外）直接透传；质量达标则接受，否则继续等待下一帧
            if (!IsSuccess(biometric)
                || (int)ExtractQualityScore(biometric.Data) >= requestedScore)
            {
                return biometric;
            }
        }

        return null;
    }

    /// <summary>
    /// requestedScore=100 的超时用例：SBI 超时后继续采集，返回质量分最高的一帧
    /// </summary>
    private async Task<BiometricInfo?> CaptureTimeoutTestAsync(FtmCaptureRequest request, int timeoutMs,
        CancellationToken cancellationToken)
    {
        var ctkDeadline = Environment.TickCount64 + timeoutMs;
        var totalDeadline = ctkDeadline + TimeoutTestExtraMs;

        BiometricInfo? best = null;
        var bestScore = -1d;

        while (Environment.TickCount64 < totalDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = totalDeadline - Environment.TickCount64;
            var nativeTimeout = (uint)Math.Clamp(remaining, MinNativeTimeoutMs, CapturePollTimeoutMs);

            var biometric = await TryCaptureOnceAsync(request, nativeTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (biometric is not null && IsSuccess(biometric) && !string.IsNullOrEmpty(biometric.Data))
            {
                var score = ExtractQualityScore(biometric.Data);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = biometric;
                }
            }

            // SBI 超时到期且已经采到过图像时立即返回，不再继续等待
            if (best is not null && Environment.TickCount64 >= ctkDeadline)
            {
                break;
            }

            await Task.Delay(CaptureRetryDelayMs, cancellationToken).ConfigureAwait(false);
        }

        return best;
    }

    /// <summary>
    /// 数量不匹配用例：只发一帧，把固件的校验错误原样返回
    /// </summary>
    private Task<BiometricInfo?> CaptureMismatchAsync(FtmCaptureRequest request, int timeoutMs,
        CancellationToken cancellationToken)
    {
        var nativeTimeout = (uint)Math.Clamp(timeoutMs, MinNativeTimeoutMs, int.MaxValue);
        return TryCaptureOnceAsync(request, nativeTimeout, cancellationToken);
    }

    /// <summary>
    /// 调用一次 CAPTURE_AUTH，并把"尚未检测到手指"转换为 null
    /// </summary>
    private async Task<BiometricInfo?> TryCaptureOnceAsync(FtmCaptureRequest request, uint timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _fap20.CaptureAuthAsync(request, timeoutMs, cancellationToken)
                .ConfigureAwait(false);

            var biometric = ParseBiometric(response);

            return biometric?.Error?.ErrorCode == NoFingerErrorCode ? null : biometric;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDeviceFailure(exception))
        {
            Debug.WriteLine($"[FAP20Proxy] CAPTURE_AUTH 失败：{exception}");
            return CreateErrorBiometric(MapDeviceFailure(exception));
        }
    }

    #endregion

    #region IFtmSecureDevice

    /// <inheritdoc />
    public async Task<Dkl1PublicKeyInfo> GetDkl1PublicKeyAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = await _fap20.GetDklPublicKeyAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.Result != FtmDklResult.Ok || result.Key is null)
        {
            throw new InvalidOperationException($"GET_DKL1_PUBLIC_KEY 失败：{result.Result}。");
        }

        return new Dkl1PublicKeyInfo
        {
            SubjectPublicKeyInfo = result.Key.SubjectPublicKeyInfo
        };
    }

    /// <inheritdoc />
    public async Task SetDkl1CertificateAsync(ReadOnlyMemory<byte> certificateDer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (certificateDer.IsEmpty)
        {
            throw new ArgumentException("DKL1 证书 DER 不能为空。", nameof(certificateDer));
        }

        var result = await _fap20.SetDklCertificateAsync(certificateDer,
            FAP20.DefaultCertificateTimeoutMs, cancellationToken).ConfigureAwait(false);

        if (result != FtmDklResult.Ok)
        {
            throw new InvalidOperationException($"SET_DKL_CERTIFICATE 失败：{result}。");
        }
    }

    /// <inheritdoc />
    public async Task LoadDomainEncryptionCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (certificateDer.IsEmpty)
        {
            throw new ArgumentException("域加密证书 DER 不能为空。", nameof(certificateDer));
        }

        await _fap20.LoadCertificateAsync(certificateDer, FAP20.DefaultCertificateTimeoutMs,
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IManualDeviceStatus

    /// <inheritdoc />
    public async Task<DeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 未连接时无法向固件查询，按 SBI 约定归为"未就绪"
        if (!_fap20.IsConnected)
        {
            return DeviceStatus.NotReady;
        }

        if (!await TryEnterOperationAsync(cancellationToken).ConfigureAwait(false))
        {
            return _lastDeviceStatus;
        }

        try
        {
            var json = await _fap20.DiscoverAsync(_hostContext.CallbackId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var response = ParseDiscoveryResponse(json);

            var status = ParseDeviceStatus(response.DeviceStatus);
            if (status is not null)
            {
                _lastDeviceStatus = status.Value;
                return status.Value;
            }

            return _lastDeviceStatus;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDeviceFailure(exception))
        {
            Debug.WriteLine($"[FAP20Proxy] 读取设备状态失败：{exception}");
            return _lastDeviceStatus;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetDeviceStatusAsync(DeviceStatus status,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nativeStatus = status switch
        {
            DeviceStatus.Ready => FtmDeviceStatus.Ready,
            DeviceStatus.Busy => FtmDeviceStatus.Busy,
            DeviceStatus.NotReady => FtmDeviceStatus.NotReady,
            DeviceStatus.NotRegistered => FtmDeviceStatus.NotRegistered,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不支持的设备状态。")
        };

        await _fap20.SetDevStatusAsync(nativeStatus, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _lastDeviceStatus = status;
    }

    #endregion

    #region Dispose

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _fap20.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    #endregion

    #region Helpers

    private async Task<bool> TryEnterOperationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
    }

    private static FtmDeviceType ResolveDeviceType(string? type, out bool invalid)
    {
        switch (type)
        {
            case null:
            case "":
            case "Biometric Device":
                invalid = false;
                return FtmDeviceType.BiometricDevice;
            case "Finger":
                invalid = false;
                return FtmDeviceType.Finger;
            default:
                // 其他类型仍调用 DISCOVER，但按 SBI1196 返回错误
                invalid = true;
                return FtmDeviceType.Finger;
        }
    }

    private static FtmEnvironment ResolveEnvironment(string? environment) => environment switch
    {
        "Developer" => FtmEnvironment.Developer,
        "Pre-Production" => FtmEnvironment.PreProduction,
        "Production" => FtmEnvironment.Production,
        _ => FtmEnvironment.Staging
    };

    private static FtmFingerPosition ResolveFingerPosition(BioInfo? entry, int index)
    {
        var subTypes = entry?.BioSubType;
        if (subTypes is not { Count: > 0 })
        {
            return FtmFingerPosition.Unknown;
        }

        var name = index < subTypes.Count ? subTypes[index] : subTypes[0];
        return FingerPositions.TryGetValue(name, out var position)
            ? position
            : FtmFingerPosition.Unknown;
    }

    private static int ResolveCaptureCount(BioInfo? entry)
    {
        var count = entry?.Count ?? 1;
        return count < 1 ? 1 : Math.Min(count, MaxBioCount);
    }

    private static int ResolveRequestedScore(BioInfo? entry)
    {
        var score = entry?.RequestedScore ?? FtmCaptureRequest.DefaultRequestedScore;
        if (double.IsNaN(score))
        {
            score = FtmCaptureRequest.DefaultRequestedScore;
        }

        return (int)Math.Clamp(score, 0, 100);
    }

    private static int CountCaptureLoops(IReadOnlyList<BioInfo>? entries)
    {
        if (entries is not { Count: > 0 })
        {
            return 1;
        }

        var total = 0;
        foreach (var entry in entries)
        {
            var count = ResolveCaptureCount(entry);
            total += count <= 9 ? count : 1;
        }

        return Math.Max(total, 1);
    }

    private static string ResolveDeviceId(BioInfo? entry, string fallback)
    {
        return string.IsNullOrWhiteSpace(entry?.DeviceId)
            ? fallback
            : NormalizeDeviceId(entry.DeviceId) ?? fallback;
    }

    private async Task<string?> ResolveDeviceIdAsync(IReadOnlyList<BioInfo>? entries,
        CancellationToken cancellationToken)
    {
        var requested = entries?
            .FirstOrDefault(static entry => !string.IsNullOrWhiteSpace(entry.DeviceId))?
            .DeviceId;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            return NormalizeDeviceId(requested);
        }

        if (!string.IsNullOrWhiteSpace(_cachedDeviceId))
        {
            return _cachedDeviceId;
        }

        try
        {
            var json = await _fap20.GetDeviceInfoAsync(_hostContext.CallbackId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var response = ParseDeviceInfoResponse(json);
            CacheIdentityFromDeviceInfo(response.DeviceInfo);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDeviceFailure(exception))
        {
            Debug.WriteLine($"[FAP20Proxy] 读取设备序列号失败：{exception}");
        }

        return _cachedDeviceId;
    }

    private static bool IsValidTransactionId(string? transactionId)
    {
        if (string.IsNullOrEmpty(transactionId) || transactionId.Length is < 4 or > 50)
        {
            return false;
        }

        foreach (var c in transactionId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlyMemory<byte>? ResolvePreviousHash(IReadOnlyList<BioInfo>? entries)
    {
        var hash = entries is { Count: > 0 } ? entries[0].PreviousHash : null;
        return ResolveHash(hash);
    }

    private static ReadOnlyMemory<byte>? ResolveHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(hash);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static BiometricInfo? ParseBiometric(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new FtmProtocolException("CAPTURE_AUTH 响应为空。");
        }

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                return null;
            }

            root = root[0];
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("biometrics", out var biometrics)
            && biometrics.ValueKind == JsonValueKind.Array)
        {
            if (biometrics.GetArrayLength() == 0)
            {
                return null;
            }

            root = biometrics[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FtmProtocolException(
                $"CAPTURE_AUTH 响应主体不是 JSON 对象（实际为 {root.ValueKind}）。");
        }

        return new BiometricInfo
        {
            SpecVersion = ReadString(root, "specVersion") ?? SbiSpecVersion,
            Data = ReadString(root, "data") ?? "",
            Hash = ReadString(root, "hash") ?? "",
            SessionKey = ReadString(root, "sessionKey") ?? "",
            Thumbprint = ReadString(root, "thumbprint") ?? "",
            Error = ReadError(root)
        };
    }

    private static double ExtractQualityScore(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return 0;
        }

        var parts = data.Split('.');
        if (parts.Length < 2)
        {
            return 0;
        }

        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));

            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("qualityScore", out var score)
                   && score.ValueKind == JsonValueKind.Number
                ? score.GetDouble()
                : 0;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return 0;
        }
    }

    private static bool IsSuccess(BiometricInfo biometric)
    {
        return biometric.Error?.ErrorCode is null or SuccessErrorCode;
    }

    private static CaptureResponse CreateErrorResponse(SbiError error)
    {
        return new CaptureResponse { Biometrics = [CreateErrorBiometric(error)] };
    }

    private static BiometricInfo CreateErrorBiometric(SbiError error)
    {
        return new BiometricInfo
        {
            SpecVersion = SbiSpecVersion,
            Data = "",
            Hash = "",
            SessionKey = "",
            Thumbprint = "",
            Error = error
        };
    }

    private static bool IsExpectedDeviceFailure(Exception exception)
    {
        return exception is FtmCommandException or FtmProtocolException or InvalidOperationException
            or JsonException or ArgumentException;
    }

    private static SbiError MapDeviceFailure(Exception exception)
    {
        if (exception is FtmCommandException { ResultCode: FAP20.DeviceBusyResultCode })
        {
            return SbiErrorHelper.DeviceBusy;
        }

        if (exception is FtmCommandException or FtmProtocolException)
        {
            return SbiErrorHelper.TechnicalErrorDuringExtraction;
        }

        return SbiErrorHelper.DeviceNotReady;
    }

    private static DeviceStatus? ParseDeviceStatus(string? status) => status?.Trim() switch
    {
        "Ready" or "Normal" => DeviceStatus.Ready,
        "Busy" => DeviceStatus.Busy,
        "Not Ready" or "NotReady" => DeviceStatus.NotReady,
        "Not Registered" or "NotRegistered" => DeviceStatus.NotRegistered,
        _ => null
    };

    private void CacheIdentity(string? deviceCode, string? digitalId)
    {
        if (!string.IsNullOrWhiteSpace(deviceCode))
        {
            _cachedDeviceId = NormalizeDeviceId(deviceCode);
            return;
        }

        CacheIdentityFromDigitalId(digitalId);
    }

    private void CacheIdentityFromDeviceInfo(string? deviceInfo)
    {
        if (!TryDecodeJwtPayload(deviceInfo, out var json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var deviceCode = ReadString(root, "deviceCode");
            if (!string.IsNullOrWhiteSpace(deviceCode))
            {
                _cachedDeviceId = NormalizeDeviceId(deviceCode);
                return;
            }

            CacheIdentityFromDigitalId(ReadString(root, "digitalId"));
        }
        catch (JsonException)
        {
            // 身份缓存失败不影响主流程
        }
    }

    private void CacheIdentityFromDigitalId(string? digitalId)
    {
        if (!TryDecodeJwtPayload(digitalId, out var json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var serialNo = ReadString(document.RootElement, "serialNo");

            if (!string.IsNullOrWhiteSpace(serialNo))
            {
                _cachedDeviceId = NormalizeDeviceId(serialNo);
            }
        }
        catch (JsonException)
        {
            // 身份缓存失败不影响主流程
        }
    }

    private static string? NormalizeDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var value = deviceId.Trim();
        return value.Length > MaxDeviceIdLength ? value[..MaxDeviceIdLength] : value;
    }

    private static bool TryDecodeJwtPayload(string? encoded, out string json)
    {
        json = "";

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split('.');
        var payload = parts.Length switch
        {
            3 => parts[1],
            1 => parts[0],
            _ => null
        };

        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        try
        {
            json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        return Convert.FromBase64String(base64);
    }

    private static DiscoveryResponse ParseDiscoveryResponse(string json)
    {
        using var document = ParseJsonDocument(json, "DISCOVER");
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

    private static DeviceInfoResponse ParseDeviceInfoResponse(string json)
    {
        using var document = ParseJsonDocument(json, "DEVICE_INFO");
        var root = RootObject(document, "DEVICE_INFO");

        return new DeviceInfoResponse
        {
            DeviceInfo = ReadString(root, "deviceInfo"),
            Error = ReadError(root)
        };
    }

    private static JsonDocument ParseJsonDocument(string json, string what)
    {
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

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadVerbatim(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

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
            return SbiErrorHelper.Success;
        }

        return new SbiError
        {
            ErrorCode = ReadVerbatim(error, "errorCode") ?? SuccessErrorCode,
            ErrorInfo = ReadString(error, "errorInfo") ?? "Success"
        };
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "...";

    #endregion
}