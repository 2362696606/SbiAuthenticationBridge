namespace SbiAuthenticationBridge.Device;

/// <summary>
/// FAP20 的设备打开方式与物理在线状态来源，由各平台实现
/// </summary>
/// <remarks>
/// Windows 通过 SetupAPI 监测并用 <c>ftm_open</c> 按配置文件枚举设备；
/// Android 通过 <c>UsbManager</c> 取得文件描述符后调用 <c>ftm_open_fd</c>。
/// </remarks>
public interface IFtmDeviceOpener : IAsyncDisposable
{
    /// <summary>
    /// 当前是否检测到设备
    /// </summary>
    bool IsPresent { get; }

    /// <summary>
    /// SDK 日志文件路径；为空时使用 SDK 默认路径
    /// </summary>
    string? LogFilePath { get; }

    /// <summary>
    /// 物理在线状态变化
    /// </summary>
    event EventHandler<bool>? PresenceChanged;

    /// <summary>
    /// 开始监测物理在线状态
    /// </summary>
    void Start();

    /// <summary>
    /// 立即探测一次物理在线状态
    /// </summary>
    /// <returns>设备在线时为 <see langword="true"/></returns>
    bool Refresh();

    /// <summary>
    /// 打开设备句柄
    /// </summary>
    /// <returns>设备句柄；失败时为 <see cref="IntPtr.Zero"/></returns>
    Task<IntPtr> OpenAsync();
}
