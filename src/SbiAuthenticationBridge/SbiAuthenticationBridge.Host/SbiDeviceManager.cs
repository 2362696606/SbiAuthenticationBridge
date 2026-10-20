namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 供界面使用的设备集合与当前选择
/// </summary>
/// <remarks>
/// 平台应用启动时创建并填入设备条目，界面通过 <see cref="SelectedDevice"/> 选择一个设备，
/// 设备控制视图订阅 <see cref="SelectedDeviceChanged"/> 跟随选择变化。
/// </remarks>
public sealed class SbiDeviceManager
{
    private readonly IReadOnlyList<SbiDeviceEntry> _devices;

    private SbiDeviceEntry? _selectedDevice;

    /// <summary>
    /// 创建设备管理器
    /// </summary>
    /// <param name="devices">设备条目，顺序即界面显示顺序</param>
    /// <exception cref="ArgumentNullException"><paramref name="devices"/> 为 null</exception>
    public SbiDeviceManager(IEnumerable<SbiDeviceEntry> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        _devices = [.. devices];
        Devices = _devices;
        _selectedDevice = _devices.Count > 0 ? _devices[0] : null;
    }

    /// <summary>
    /// 当前选择发生变化
    /// </summary>
    public event EventHandler? SelectedDeviceChanged;

    /// <summary>
    /// 全部设备条目，顺序与构造时一致
    /// </summary>
    public IReadOnlyList<SbiDeviceEntry> Devices { get; }

    /// <summary>
    /// 当前操作设备
    /// </summary>
    /// <exception cref="ArgumentException">赋值的条目不属于本管理器</exception>
    public SbiDeviceEntry? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            if (value is not null && !_devices.Contains(value))
            {
                throw new ArgumentException("设备条目不属于当前管理器。", nameof(value));
            }

            _selectedDevice = value;
            SelectedDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
