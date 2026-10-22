using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

public interface ISbiDevice
{
    /// <summary>
    /// 发现设备服务
    /// </summary>
    /// <param name="request">请求体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应体</returns>
    Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取设备信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应体</returns>
    Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
}