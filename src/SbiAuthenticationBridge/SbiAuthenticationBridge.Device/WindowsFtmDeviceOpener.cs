using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// Windows 实现：SetupAPI 监测在线状态，<c>ftm_open</c> 按 FingerPrintDebug.INI 打开设备
/// </summary>
public sealed class WindowsFtmDeviceOpener : IFtmDeviceOpener
{
    private readonly UsbPresenceMonitor _presenceMonitor;

    /// <summary>
    /// 创建 Windows 设备打开器
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID</param>
    /// <param name="productId">USB 产品 ID</param>
    public WindowsFtmDeviceOpener(ushort vendorId, ushort productId)
    {
        _presenceMonitor = new UsbPresenceMonitor(vendorId, productId);
        _presenceMonitor.PresenceChanged += OnPresenceChanged;
    }

    /// <inheritdoc />
    public bool IsPresent => _presenceMonitor.IsPresent;

    /// <inheritdoc />
    public string? LogFilePath => null;

    /// <inheritdoc />
    public event EventHandler<bool>? PresenceChanged;

    /// <inheritdoc />
    public void Start() => _presenceMonitor.Start();

    /// <inheritdoc />
    public bool Refresh() => _presenceMonitor.Refresh();

    /// <inheritdoc />
    public Task<IntPtr> OpenAsync() => Task.Run(FtmNativeMethods.Open);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _presenceMonitor.PresenceChanged -= OnPresenceChanged;
        return _presenceMonitor.DisposeAsync();
    }

    private void OnPresenceChanged(object? sender, bool isPresent) => PresenceChanged?.Invoke(this, isPresent);
}
