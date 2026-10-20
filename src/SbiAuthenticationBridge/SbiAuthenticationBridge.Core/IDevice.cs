using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Core;

public interface IDevice : IAsyncDisposable
{
    /// <summary>
    /// 异步连接设备
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// 异步断开设备
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 获取设备连接状态，设备应自行检测物理连接。若物理连接断开应反应在此属性上
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 设置设备工作状态
    /// </summary>
    /// <param name="status">目标状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default);
}