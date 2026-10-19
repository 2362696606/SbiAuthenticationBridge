using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

public interface IAuthenticationDeviceHandle : ISbiDeviceHandle
{
    /// <summary>
    /// 认证设备Capture
    /// </summary>
    /// <param name="request">capture请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>capture响应</returns>
    Task<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}