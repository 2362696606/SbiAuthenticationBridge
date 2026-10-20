using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 尚未注册任何服务实现时使用的空句柄：发现返回空列表，信息查询返回“无设备”错误
/// </summary>
internal sealed class NullSbiServiceHandle : ISbiServiceHandle
{
    internal static readonly NullSbiServiceHandle Instance = new();

    private NullSbiServiceHandle()
    {
    }

    public Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DiscoveryResponse>>([]);
    }

    public Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DeviceInfoResponse>>(
            [new DeviceInfoResponse { Error = SbiErrorHelper.NoDeviceConnected }]);
    }
}
