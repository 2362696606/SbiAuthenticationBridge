using Prism.Events;

namespace SbiAuthenticationBridge.Events;

/// <summary>
/// 设备连接状态变化事件
/// </summary>
public sealed class DeviceConnectionChangedEvent : PubSubEvent<DeviceConnectionChangedEventArgs>
{
}

/// <summary>
/// 设备连接状态变化事件参数
/// </summary>
/// <param name="DeviceId">设备提供者分配的内部标识</param>
/// <param name="IsConnected">变化后的连接状态</param>
public sealed record DeviceConnectionChangedEventArgs(string DeviceId, bool IsConnected);
