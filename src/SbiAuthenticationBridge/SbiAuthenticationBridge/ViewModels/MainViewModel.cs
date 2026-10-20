namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 主界面：承载设备列表、设备控制、服务控制与运行日志四个面板
/// </summary>
public class MainViewModel : ViewModelBase
{
    /// <summary>
    /// 创建主界面视图模型
    /// </summary>
    /// <param name="devices">设备列表</param>
    /// <param name="device">设备控制</param>
    /// <param name="sbiService">SBI 服务控制</param>
    /// <param name="log">运行日志</param>
    public MainViewModel(DevicesViewModel devices, DeviceViewModel device,
        SbiServiceViewModel sbiService, LogViewModel log)
    {
        Devices = devices;
        Device = device;
        SbiService = sbiService;
        Log = log;
    }

    /// <summary>
    /// 设备列表视图模型
    /// </summary>
    public DevicesViewModel Devices { get; }

    /// <summary>
    /// 设备控制视图模型
    /// </summary>
    public DeviceViewModel Device { get; }

    /// <summary>
    /// SBI 服务控制视图模型
    /// </summary>
    public SbiServiceViewModel SbiService { get; }

    /// <summary>
    /// 运行日志视图模型
    /// </summary>
    public LogViewModel Log { get; }
}
