using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 面向注册服务的 SBI 服务句柄（<c>/device</c>、<c>/info</c>、<c>/rcapture</c>、<c>/stream</c>）
/// </summary>
/// <remarks>
/// 内部只通过 <see cref="IRegisterSbiDevice"/> 访问设备。当前尚无注册设备实现，
/// 但服务层编排已就绪：注册设备接入后可直接复用。
/// </remarks>
public sealed class RegisterSbiServiceHandle
    : SbiServiceHandleBase<IRegisterSbiDevice>, IRegisterSbiServiceHandle
{
    /// <summary>
    /// 创建注册服务句柄
    /// </summary>
    /// <param name="devices">注册设备句柄集合</param>
    public RegisterSbiServiceHandle(IEnumerable<IRegisterSbiDevice> devices)
        : base(devices)
    {
    }

    /// <summary>
    /// 注册采集（<c>/rcapture</c>）
    /// </summary>
    /// <param name="request">注册采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>与请求 bio 项一一对应的明文采集结果</returns>
    public Task<CaptureResponse> CaptureAsync(CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        return CaptureCoreAsync(request,
            static (device, singleRequest, token) => device.CaptureAsync(singleRequest, token),
            cancellationToken);
    }

    /// <summary>
    /// 打开注册设备预览流（<c>/stream</c>）
    /// </summary>
    /// <param name="request">预览流请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备返回的 M-JPEG 流</returns>
    /// <exception cref="SbiException">无可用设备或设备无法提供流</exception>
    public async Task<Stream> StreamAsync(StreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var device = await ResolveDeviceAsync(request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            throw new SbiException(HasDevices
                ? SbiErrorHelper.DeviceNotFound
                : SbiErrorHelper.NoDeviceConnected);
        }

        try
        {
            var stream = await device.StreamAsync(request, cancellationToken).ConfigureAwait(false);
            return stream ?? throw new SbiException(SbiErrorHelper.TechnicalErrorDuringExtraction);
        }
        catch (SbiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            throw new SbiException(SbiErrorHelper.DeviceNotReady, exception);
        }
        catch (Exception exception)
        {
            throw new SbiException(SbiErrorHelper.TechnicalErrorDuringExtraction, exception);
        }
    }
}
