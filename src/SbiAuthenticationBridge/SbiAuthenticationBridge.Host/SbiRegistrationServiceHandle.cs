using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 注册用途的 SBI 服务实现
/// </summary>
internal sealed class SbiRegistrationServiceHandle : SbiServiceHandleBase, ISbiRegistrationServiceHandle
{
    /// <summary>
    /// 创建注册服务句柄
    /// </summary>
    /// <param name="deviceProvider">设备提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiRegistrationServiceHandle(
        ISbiDeviceProvider deviceProvider,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
        : base(deviceProvider, hostContext, loggerFactory)
    {
    }

    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Registration;

    /// <inheritdoc />
    protected override bool IsSupported(ISbiDevice device) => device is ISbiRegistrationDevice;

    /// <inheritdoc />
    public Task<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CaptureCoreAsync(
            "CAPTURE",
            request,
            GetDevices(),
            static (device, subRequest, token) =>
                ((ISbiRegistrationDevice)device).CaptureAsync(subRequest, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> StreamAsync(
        StreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequest("STREAM", request);

        var devices = GetDevices();
        var device = await SbiDeviceSelector
            .SelectAsync(devices, request.DeviceId, null, request.DeviceSubId, Logger, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            var error = devices.Count == 0
                ? SbiErrorHelper.NoDeviceConnected
                : SbiErrorHelper.DeviceNotFound;

            Logger.LogWarning(
                "STREAM 无法路由：deviceId={DeviceId}，错误码={ErrorCode}",
                request.DeviceId,
                error.ErrorCode);

            throw new SbiException(error);
        }

        Logger.LogInformation(
            "STREAM 打开：deviceId={DeviceId}，subId={SubId} -> {Device}",
            request.DeviceId,
            request.DeviceSubId,
            device.GetType().Name);

        try
        {
            var stream = await ((ISbiRegistrationDevice)device)
                .StreamAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (stream is null)
            {
                throw new SbiException(SbiErrorHelper.Custom(500, "Device returned no stream"));
            }

            Logger.LogDebug("STREAM 响应体：<stream> deviceId={DeviceId}", request.DeviceId);
            return stream;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SbiException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "STREAM 设备异常：{Device}", device.GetType().Name);
            throw new SbiException(SbiErrorHelper.Custom(500, "Device failure"), exception);
        }
    }
}
