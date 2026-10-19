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
}