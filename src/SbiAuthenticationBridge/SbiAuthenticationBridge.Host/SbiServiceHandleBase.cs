using System.Collections.Concurrent;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using Serilog;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 面向服务面的设备句柄基类：汇总多个设备的结果，并负责把采集项路由到具体设备。
/// </summary>
/// <remarks>
/// <para>
/// 派生类只需给出设备集合以及“如何在某台设备上执行一次采集”，
/// 设备发现、设备信息、按 <see cref="BioInfo.DeviceId"/> 路由、逐项错误封装都由本类处理。
/// </para>
/// <para>
/// 单台设备的失败不会使整体调用失败：发现与信息查询会为失败设备补一条带
/// <see cref="SbiError"/> 的记录；采集会为对应 bio 项返回一条错误记录。
/// </para>
/// </remarks>
/// <typeparam name="TDevice">设备句柄类型，须实现 <see cref="ISbiDevice"/></typeparam>
public abstract class SbiServiceHandleBase<TDevice> : ISbiServiceHandle, IDisposable
    where TDevice : ISbiDevice
{
    private const string DeviceNotFoundCode = "106";

    private const string DeviceNotReadyCode = "110";

    private const string AnyBiometricDevice = "Biometric Device";

    private readonly ILogger _logger = Log.ForContext("SourceContext", "Host");

    private readonly TDevice[] _devices;

    private readonly ConcurrentDictionary<string, TDevice> _devicesById = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _deviceIdGate = new(1, 1);

    private volatile bool _deviceIdsLoaded;

    /// <summary>
    /// 创建服务面句柄基类
    /// </summary>
    /// <param name="devices">受管的设备句柄，顺序即响应顺序</param>
    /// <exception cref="ArgumentNullException"><paramref name="devices"/> 为 null</exception>
    protected SbiServiceHandleBase(IEnumerable<TDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        _devices = [.. devices];
        Devices = _devices;
    }

    /// <summary>
    /// 受管设备，顺序与构造时一致
    /// </summary>
    protected IReadOnlyList<TDevice> Devices { get; }

    /// <summary>
    /// 是否至少注册了一台设备
    /// </summary>
    protected bool HasDevices => _devices.Length > 0;

    /// <summary>
    /// 句柄是否已释放
    /// </summary>
    protected bool IsDisposed { get; private set; }

    /// <summary>
    /// 设备发现（<c>/device</c>）：按注册顺序汇总每台设备的结果
    /// </summary>
    /// <param name="request">发现请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有应答设备的发现结果；失败设备以一条错误记录返回</returns>
    public async Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _logger.Debug("开始设备发现，请求类型 {Type:l}", request.Type);

        var results = new List<DiscoveryResponse>();

        foreach (var device in _devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var responses = await device.DiscoverAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (responses.Count == 0)
                {
                    // 类型不匹配或物理不在线的设备不应答，直接从结果中过滤掉
                    continue;
                }

                foreach (var response in responses)
                {
                    RememberDeviceId(response.DeviceId, device);
                    results.Add(response);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "设备发现失败");
                results.Add(new DiscoveryResponse { Error = SbiErrorHelper.DeviceNotReady });
            }
        }

        _logger.Debug("设备发现完成，返回 {Count} 条结果", results.Count);
        return results;
    }

    /// <summary>
    /// 获取设备信息（<c>/info</c>）：按注册顺序汇总每台设备的结果
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有设备的信息；失败设备以一条错误记录返回</returns>
    public async Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!HasDevices)
        {
            return [new DeviceInfoResponse { Error = SbiErrorHelper.NoDeviceConnected }];
        }

        var results = new List<DeviceInfoResponse>();

        foreach (var device in _devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var responses = await device.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
                results.AddRange(responses);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "获取设备信息失败");
                results.Add(new DeviceInfoResponse { Error = SbiErrorHelper.DeviceNotReady });
            }
        }

        return results;
    }

    /// <summary>
    /// 采集编排：为请求中的每个 bio 项选一台设备采集，并保持响应项与请求项一一对应
    /// </summary>
    /// <param name="request">采集请求</param>
    /// <param name="capture">在指定设备上执行采集</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>采集响应</returns>
    protected async Task<CaptureResponse> CaptureCoreAsync(CaptureRequest request,
        Func<TDevice, CaptureRequest, CancellationToken, Task<CaptureResponse>> capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capture);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var bios = request.Bio;
        if (bios is null || bios.Count == 0)
        {
            return new CaptureResponse
            {
                Biometrics = [new BiometricInfo { Error = SbiErrorHelper.RequestedBiometricCountNotSupported }]
            };
        }

        if (!HasDevices)
        {
            return CreateErrorResponse(bios.Count, static () => SbiErrorHelper.NoDeviceConnected);
        }

        _logger.Information("开始采集，共 {Count} 个 bio 项", bios.Count);

        var biometrics = new List<BiometricInfo>(bios.Count);

        foreach (var bio in bios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            biometrics.Add(await CaptureOneAsync(request, bio, capture, cancellationToken)
                .ConfigureAwait(false));
        }

        _logger.Information("采集完成，返回 {Count} 条结果", biometrics.Count);
        return new CaptureResponse { Biometrics = biometrics };
    }

    /// <summary>
    /// 按 deviceId 查找设备；未指定时返回全部设备
    /// </summary>
    /// <param name="deviceId">设备 ID；为空表示不限定</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>候选设备</returns>
    protected async Task<IReadOnlyList<TDevice>> ResolveCandidatesAsync(string? deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return _devices;
        }

        if (_devicesById.TryGetValue(deviceId, out var device))
        {
            return [device];
        }

        await LoadDeviceIdsAsync(cancellationToken).ConfigureAwait(false);

        return _devicesById.TryGetValue(deviceId, out device) ? [device] : [];
    }

    /// <summary>
    /// 按 deviceId 查找设备；为空时返回第一台设备
    /// </summary>
    /// <param name="deviceId">设备 ID；为空表示不限定</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>目标设备；没有可用设备时返回 default</returns>
    protected async Task<TDevice?> ResolveDeviceAsync(string? deviceId, CancellationToken cancellationToken)
    {
        var candidates = await ResolveCandidatesAsync(deviceId, cancellationToken).ConfigureAwait(false);
        return candidates.Count > 0 ? candidates[0] : default;
    }

    /// <summary>
    /// 释放句柄；不影响调用方持有的设备实例
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _deviceIdGate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 采集单个 bio 项：按 deviceId 或注册顺序选择设备，遇到“设备不匹配/未就绪”时尝试下一台
    /// </summary>
    private async Task<BiometricInfo> CaptureOneAsync(CaptureRequest request, BioInfo bio,
        Func<TDevice, CaptureRequest, CancellationToken, Task<CaptureResponse>> capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bio);

        var candidates = await ResolveCandidatesAsync(bio.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return new BiometricInfo { Error = SbiErrorHelper.DeviceNotFound };
        }

        var singleBioRequest = CreateSingleBioRequest(request, bio);
        BiometricInfo? lastFailure = null;

        foreach (var device in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await capture(device, singleBioRequest, cancellationToken)
                    .ConfigureAwait(false);

                var biometric = response.Biometrics is { Count: > 0 } biometrics
                    ? biometrics[0]
                    : null;

                if (biometric is null)
                {
                    lastFailure = new BiometricInfo
                    {
                        Error = SbiErrorHelper.TechnicalErrorDuringExtraction
                    };
                    continue;
                }

                // 多设备时，能力不匹配或未就绪的设备交给下一台继续尝试
                if (candidates.Count > 1
                    && biometric.Error?.ErrorCode is DeviceNotFoundCode or DeviceNotReadyCode)
                {
                    lastFailure = biometric;
                    continue;
                }

                return biometric;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ObjectDisposedException exception)
            {
                _logger.Warning(exception, "采集设备已释放");
                lastFailure = new BiometricInfo { Error = SbiErrorHelper.DeviceNotReady };
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "采集失败");
                lastFailure = new BiometricInfo
                {
                    Error = SbiErrorHelper.TechnicalErrorDuringExtraction
                };
            }
        }

        return lastFailure ?? new BiometricInfo { Error = SbiErrorHelper.DeviceNotFound };
    }

    /// <summary>
    /// 懒加载设备 ID 映射；供按 deviceId 路由使用
    /// </summary>
    private async Task LoadDeviceIdsAsync(CancellationToken cancellationToken)
    {
        if (_deviceIdsLoaded)
        {
            return;
        }

        await _deviceIdGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_deviceIdsLoaded)
            {
                return;
            }

            foreach (var device in _devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var responses = await device.DiscoverAsync(
                        new DiscoveryRequest { Type = AnyBiometricDevice },
                        cancellationToken).ConfigureAwait(false);

                    foreach (var response in responses)
                    {
                        RememberDeviceId(response.DeviceId, device);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "查询设备 ID 失败");
                }
            }

            _deviceIdsLoaded = true;
        }
        finally
        {
            _deviceIdGate.Release();
        }
    }

    /// <summary>
    /// 记录设备 ID 到设备句柄的映射
    /// </summary>
    private void RememberDeviceId(string? deviceId, TDevice device)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            _devicesById[deviceId] = device;
        }
    }

    /// <summary>
    /// 复制请求，只保留当前 bio 项
    /// </summary>
    private static CaptureRequest CreateSingleBioRequest(CaptureRequest request, BioInfo bio) => new()
    {
        Env = request.Env,
        Purpose = request.Purpose,
        SpecVersion = request.SpecVersion,
        Timeout = request.Timeout,
        CaptureTime = request.CaptureTime,
        DomainUri = request.DomainUri,
        TransactionId = request.TransactionId,
        Bio = [bio],
        CustomOpts = request.CustomOpts
    };

    /// <summary>
    /// 构造指定项数的错误响应
    /// </summary>
    private static CaptureResponse CreateErrorResponse(int count, Func<SbiError> errorFactory)
    {
        var biometrics = new List<BiometricInfo>(count);

        for (var index = 0; index < count; index++)
        {
            biometrics.Add(new BiometricInfo { Error = errorFactory() });
        }

        return new CaptureResponse { Biometrics = biometrics };
    }
}
