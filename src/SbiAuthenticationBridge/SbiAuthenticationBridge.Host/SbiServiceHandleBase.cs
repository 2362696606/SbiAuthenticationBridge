using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// SBI 服务面公共实现：设备枚举、发现/信息聚合、采集路由与日志
/// </summary>
internal abstract class SbiServiceHandleBase : ISbiServiceHandle
{
    /// <summary>
    /// 采集响应使用的 SBI 规范版本
    /// </summary>
    protected const string SbiSpecVersion = "0.9.5";

    private readonly ISbiDeviceProvider _deviceProvider;
    private readonly ISbiHostContext _hostContext;

    /// <summary>
    /// 创建服务句柄
    /// </summary>
    /// <param name="deviceProvider">设备提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    protected SbiServiceHandleBase(
        ISbiDeviceProvider deviceProvider,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(deviceProvider);
        ArgumentNullException.ThrowIfNull(hostContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _deviceProvider = deviceProvider;
        _hostContext = hostContext;
        Logger = loggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// 本服务对应的设备用途
    /// </summary>
    protected abstract DevicePurpose Purpose { get; }

    /// <summary>
    /// 日志器
    /// </summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public string CallbackId => _hostContext.GetCallbackId(Purpose);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(
        DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequest("DISCOVER", request);

        var devices = GetDevices();
        Logger.LogDebug(
            "DISCOVER 开始：类型={Type}，设备数={DeviceCount}",
            request.Type,
            devices.Count);

        var responses = new List<DiscoveryResponse>();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var deviceResponses = await device
                    .DiscoverAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                responses.AddRange(deviceResponses);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SbiException exception)
            {
                Logger.LogWarning(
                    exception,
                    "DISCOVER 设备业务错误：{Device}，错误码={ErrorCode}",
                    device.GetType().Name,
                    exception.Error.ErrorCode);

                responses.Add(new DiscoveryResponse { Error = exception.Error });
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "DISCOVER 设备异常：{Device}", device.GetType().Name);
                responses.Add(new DiscoveryResponse
                {
                    Error = SbiErrorHelper.Custom(500, "Device failure"),
                });
            }
        }

        Logger.LogInformation("DISCOVER 完成：类型={Type}，结果数={Count}", request.Type, responses.Count);
        LogResponse("DISCOVER", responses);
        return responses;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = GetDevices();
        Logger.LogDebug("INFO 开始：设备数={DeviceCount}", devices.Count);

        var responses = new List<DeviceInfoResponse>();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var deviceResponses = await device
                    .GetDeviceInfoAsync(cancellationToken)
                    .ConfigureAwait(false);

                responses.AddRange(deviceResponses);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SbiException exception)
            {
                Logger.LogWarning(
                    exception,
                    "INFO 设备业务错误：{Device}，错误码={ErrorCode}",
                    device.GetType().Name,
                    exception.Error.ErrorCode);

                responses.Add(new DeviceInfoResponse { Error = exception.Error });
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "INFO 设备异常：{Device}", device.GetType().Name);
                responses.Add(new DeviceInfoResponse
                {
                    Error = SbiErrorHelper.Custom(500, "Device failure"),
                });
            }
        }

        Logger.LogInformation("INFO 完成：结果数={Count}", responses.Count);
        LogResponse("INFO", responses);
        return responses;
    }

    /// <summary>
    /// 采集前准备已路由的设备；默认无操作
    /// </summary>
    /// <param name="device">已路由到的设备</param>
    /// <param name="cancellationToken">取消令牌</param>
    protected virtual Task PrepareDeviceAsync(
        ISbiDevice device,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 按 bio 项路由并合并采集结果
    /// </summary>
    /// <param name="operation">操作名称，用于日志</param>
    /// <param name="request">采集请求</param>
    /// <param name="devices">候选设备</param>
    /// <param name="captureAsync">调用具体设备采集的委托</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>合并后的采集响应</returns>
    protected async Task<CaptureResponse> CaptureCoreAsync(
        string operation,
        CaptureRequest request,
        IReadOnlyList<ISbiDevice> devices,
        Func<ISbiDevice, CaptureRequest, CancellationToken, Task<CaptureResponse>> captureAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captureAsync);
        LogRequest(operation, request);

        var biometrics = new List<BiometricInfo>();
        if (request.Bio is not { Count: > 0 })
        {
            Logger.LogWarning("{Operation} 请求未包含 bio 项", operation);

            var emptyResponse = new CaptureResponse { Biometrics = biometrics };
            LogResponse(operation, emptyResponse);
            return emptyResponse;
        }

        foreach (var bio in request.Bio)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var device = await SbiDeviceSelector
                .SelectAsync(devices, bio.DeviceId, bio.Type, bio.DeviceSubId, Logger, cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                var error = devices.Count == 0
                    ? SbiErrorHelper.NoDeviceConnected
                    : SbiErrorHelper.DeviceNotFound;

                Logger.LogWarning(
                    "{Operation} 无法路由 bio：type={Type}，deviceId={DeviceId}，错误码={ErrorCode}",
                    operation,
                    bio.Type,
                    bio.DeviceId,
                    error.ErrorCode);

                biometrics.Add(CreateErrorBiometric(error));
                continue;
            }

            Logger.LogDebug(
                "{Operation} 路由：type={Type}，deviceId={DeviceId}，subId={SubId} -> {Device}",
                operation,
                bio.Type,
                bio.DeviceId,
                bio.DeviceSubId,
                device.GetType().Name);

            try
            {
                await PrepareDeviceAsync(device, cancellationToken).ConfigureAwait(false);

                var response = await captureAsync(device, CreateSubRequest(request, bio), cancellationToken)
                    .ConfigureAwait(false);

                if (response.Biometrics is { Count: > 0 })
                {
                    biometrics.AddRange(response.Biometrics);
                }
                else
                {
                    Logger.LogWarning("{Operation} 设备返回空结果：{Device}", operation, device.GetType().Name);
                    biometrics.Add(CreateErrorBiometric(SbiErrorHelper.TechnicalErrorDuringExtraction));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SbiException exception)
            {
                Logger.LogWarning(
                    exception,
                    "{Operation} 设备业务错误：{Device}，错误码={ErrorCode}",
                    operation,
                    device.GetType().Name,
                    exception.Error.ErrorCode);

                biometrics.Add(CreateErrorBiometric(exception.Error));
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "{Operation} 设备异常：{Device}", operation, device.GetType().Name);
                biometrics.Add(CreateErrorBiometric(SbiErrorHelper.Custom(500, "Device failure")));
            }
        }

        var result = new CaptureResponse { Biometrics = biometrics };
        Logger.LogInformation(
            "{Operation} 完成：bio 项数={BioCount}，返回条数={ResultCount}",
            operation,
            request.Bio.Count,
            biometrics.Count);

        LogResponse(operation, result);
        return result;
    }

    /// <summary>
    /// 判断设备是否属于本服务用途
    /// </summary>
    /// <param name="device">待判断的设备</param>
    /// <returns>属于本用途时为 <see langword="true"/></returns>
    protected abstract bool IsSupported(ISbiDevice device);

    /// <summary>
    /// 获取本用途设备，按内部键稳定排序
    /// </summary>
    /// <returns>候选设备列表</returns>
    protected IReadOnlyList<ISbiDevice> GetDevices() =>
        _deviceProvider.Devices
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Where(IsSupported)
            .ToList();

    /// <summary>
    /// 构造携带错误的采集条目
    /// </summary>
    /// <param name="error">SBI 错误对象</param>
    /// <returns>采集条目</returns>
    protected static BiometricInfo CreateErrorBiometric(SbiError error) => new()
    {
        SpecVersion = SbiSpecVersion,
        Data = string.Empty,
        Hash = string.Empty,
        // 错误响应不携带认证专属字段；Registration 错误 Schema 不允许这两个字段出现
        SessionKey = null,
        Thumbprint = null,
        Error = error,
    };

    /// <summary>
    /// 记录请求体
    /// </summary>
    /// <param name="operation">操作名称</param>
    /// <param name="request">请求对象</param>
    protected void LogRequest(string operation, object? request)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("{Operation} 请求体：{Body}", operation, SbiLogBody.Serialize(request));
        }
    }

    /// <summary>
    /// 记录响应体
    /// </summary>
    /// <param name="operation">操作名称</param>
    /// <param name="response">响应对象</param>
    protected void LogResponse(string operation, object? response)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("{Operation} 响应体：{Body}", operation, SbiLogBody.Serialize(response));
        }
    }

    private static CaptureRequest CreateSubRequest(CaptureRequest request, BioInfo bio) => new()
    {
        Env = request.Env,
        Purpose = request.Purpose,
        SpecVersion = request.SpecVersion,
        Timeout = request.Timeout,
        CaptureTime = request.CaptureTime,
        DomainUri = request.DomainUri,
        TransactionId = request.TransactionId,
        Bio = [bio],
        CustomOpts = request.CustomOpts,
    };
}
