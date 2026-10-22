using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 认证设备
/// </summary>
public interface ISbiAuthenticationDevice : ISbiDevice
{
    /// <summary>
    /// 认证设备Capture
    /// </summary>
    /// <param name="request">capture请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>capture响应</returns>
    Task<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}