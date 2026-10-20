using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbiAuthenticationBridge.Host;
using Serilog;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备列表：枚举平台注册的设备并维护“当前操作设备”
/// </summary>
public partial class DevicesViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "UI");

    private readonly DispatcherTimer _stateTimer;

    private readonly SbiDeviceManager? _manager;

    /// <summary>
    /// 创建设备列表视图模型并立即加载一次
    /// </summary>
    /// <param name="managers">平台注册的设备管理器；当前平台未提供时为空集合</param>
    public DevicesViewModel(IEnumerable<SbiDeviceManager> managers)
    {
        ArgumentNullException.ThrowIfNull(managers);

        _manager = managers.FirstOrDefault();
        Refresh();

        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stateTimer.Tick += OnStateTimerTick;
        _stateTimer.Start();
    }

    /// <summary>
    /// 设备列表
    /// </summary>
    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    /// <summary>
    /// 当前操作设备；选择变化会同步到 <see cref="SbiDeviceManager.SelectedDevice"/>
    /// </summary>
    [ObservableProperty]
    public partial DeviceItemViewModel? SelectedDevice { get; set; }

    /// <summary>
    /// 加载或操作失败时的提示
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否存在可用设备
    /// </summary>
    public bool HasDevices => Devices.Count > 0;

    /// <summary>
    /// 重新枚举平台注册的设备
    /// </summary>
    public void Refresh()
    {
        ErrorMessage = null;

        SelectedDevice = null;
        Devices.Clear();

        if (_manager is null)
        {
            Logger.Warning("当前平台未注册设备");
            ErrorMessage = "当前平台未注册设备。";
            OnPropertyChanged(nameof(HasDevices));
            return;
        }

        foreach (var entry in _manager.Devices)
        {
            Devices.Add(new DeviceItemViewModel(entry));
        }

        if (Devices.Count == 0)
        {
            ErrorMessage = "没有可用的设备。";
        }

        SelectedDevice = Devices.FirstOrDefault(item =>
                             ReferenceEquals(item.Entry, _manager.SelectedDevice))
                         ?? Devices.FirstOrDefault();

        if (SelectedDevice is not null)
        {
            _manager.SelectedDevice = SelectedDevice.Entry;
        }

        Logger.Information("设备列表已刷新，共 {Count} 台", Devices.Count);
        OnPropertyChanged(nameof(HasDevices));
    }

    /// <summary>
    /// 刷新命令
    /// </summary>
    [RelayCommand]
    private void RefreshDevices() => Refresh();

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        if (_manager is not null && value is not null)
        {
            _manager.SelectedDevice = value.Entry;
            Logger.Information("当前操作设备：{Device:l}", value.Name);
        }
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        foreach (var device in Devices)
        {
            device.RefreshState();
        }
    }
}
