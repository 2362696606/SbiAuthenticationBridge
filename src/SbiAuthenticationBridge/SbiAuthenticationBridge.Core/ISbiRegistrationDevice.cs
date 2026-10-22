using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

public interface ISbiRegistrationDevice:ISbiDevice
{
    /// <summary>
    /// 注册设备Capture
    /// </summary>
    /// <param name="request">注册设备请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册设备响应</returns>
    Task<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 打开注册设备预览流
    /// </summary>
    /// <param name="request">预览流请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>M-JPEG 视频流；由调用方负责释放</returns>
    /// <exception cref="SbiException">
    /// 无法提供预览流时抛出，异常携带的 <see cref="SbiException.Error"/> 即应返回给调用方的 SBI 错误。
    /// </exception>
    Task<Stream> StreamAsync(StreamRequest request, CancellationToken cancellationToken = default);
}
