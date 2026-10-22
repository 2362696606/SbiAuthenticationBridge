using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 设备状态控制
/// </summary>
public interface IManualDeviceStatus
{
    /// <summary>
    /// 获取设备状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备状态</returns>
    Task<DeviceStatus> GetDeviceStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置设备状态
    /// </summary>
    /// <param name="status">设备状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设置完成的任务</returns>
    Task SetDeviceStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default);
}