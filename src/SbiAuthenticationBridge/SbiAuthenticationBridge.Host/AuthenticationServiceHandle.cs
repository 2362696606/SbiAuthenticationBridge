using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 面向认证服务的 SBI 服务句柄（<c>/device</c>、<c>/info</c>、<c>/capture</c>）
/// </summary>
/// <remarks>
/// 内部只通过 <see cref="IAuthenticationDevice"/>（即 <see cref="ISbiDevice"/> 的认证扩展）
/// 访问设备，不依赖具体设备实现（如 FAP20）。设备实例由宿主应用创建并注入。
/// </remarks>
public sealed class AuthenticationServiceHandle
    : SbiServiceHandleBase<IAuthenticationDevice>, IAuthenticationServiceHandle
{
    /// <summary>
    /// 创建认证服务句柄
    /// </summary>
    /// <param name="devices">认证设备句柄集合</param>
    public AuthenticationServiceHandle(IEnumerable<IAuthenticationDevice> devices)
        : base(devices)
    {
    }

    /// <summary>
    /// 认证采集（<c>/capture</c>）
    /// </summary>
    /// <param name="request">认证采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>与请求 bio 项一一对应的采集结果</returns>
    public Task<CaptureResponse> CaptureAsync(CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        return CaptureCoreAsync(request,
            static (device, singleRequest, token) => device.CaptureAsync(singleRequest, token),
            cancellationToken);
    }
}
