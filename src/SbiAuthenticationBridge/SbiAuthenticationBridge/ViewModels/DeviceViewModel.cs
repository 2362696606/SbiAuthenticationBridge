using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Host;
using Serilog;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备状态选项：用于下拉框显示中文名称
/// </summary>
/// <param name="Value">SBI 设备状态</param>
/// <param name="DisplayName">界面显示文本</param>
public sealed record DeviceStatusOption(DeviceStatus Value, string DisplayName);

/// <summary>
/// 设备控制：连接/断开当前设备，并设置设备工作状态
/// </summary>
public partial class DeviceViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "UI");

    private readonly DispatcherTimer _stateTimer;

    private readonly SbiDeviceManager? _manager;

    private SbiDeviceEntry? _entry;

    /// <summary>
    /// 创建设备控制视图模型并跟随设备列表的当前选择
    /// </summary>
    /// <param name="managers">平台注册的设备管理器；当前平台未提供时为空集合</param>
    public DeviceViewModel(IEnumerable<SbiDeviceManager> managers)
    {
        ArgumentNullException.ThrowIfNull(managers);

        _manager = managers.FirstOrDefault();

        if (_manager is not null)
        {
            _manager.SelectedDeviceChanged += OnSelectedDeviceChanged;
        }

        StatusOptions =
        [
            new DeviceStatusOption(DeviceStatus.Ready, "Ready（就绪）"),
            new DeviceStatusOption(DeviceStatus.Busy, "Busy（忙）"),
            new DeviceStatusOption(DeviceStatus.NotReady, "Not Ready（未就绪）"),
            new DeviceStatusOption(DeviceStatus.NotRegistered, "Not Registered（未注册）")
        ];

        SelectedStatus = StatusOptions[0];
        Refresh();

        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stateTimer.Tick += OnStateTimerTick;
        _stateTimer.Start();
    }

    /// <summary>
    /// 可设置的设备状态
    /// </summary>
    public IReadOnlyList<DeviceStatusOption> StatusOptions { get; }

    /// <summary>
    /// 当前选中的目标状态
    /// </summary>
    [ObservableProperty]
    public partial DeviceStatusOption? SelectedStatus { get; set; }

    /// <summary>
    /// 操作失败时的提示
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 当前设备名称
    /// </summary>
    public string DeviceName => _entry?.Name ?? "未选择设备";

    /// <summary>
    /// 设备角色
    /// </summary>
    public string RoleText => _entry?.SbiDevice switch
    {
        IAuthenticationDevice => "认证设备",
        IRegisterSbiDevice => "注册设备",
        null => "设备",
        _ => "SBI 设备"
    };

    /// <summary>
    /// 是否已选择设备
    /// </summary>
    public bool HasDevice => _entry is not null;

    /// <summary>
    /// 当前设备是否已连接
    /// </summary>
    public bool IsConnected => _entry?.Device.IsConnected ?? false;

    /// <summary>
    /// 连接状态文本
    /// </summary>
    public string ConnectionText => !HasDevice
        ? "—"
        : IsConnected
            ? "已连接"
            : "未连接";

    /// <summary>
    /// 当前设备是否支持更换密钥
    /// </summary>
    public bool HasKeyRotation => _entry?.KeyRotation is not null;

    /// <summary>
    /// 更换密钥是否需要选择证书文件
    /// </summary>
    public bool RequiresKeyCertificate => _entry?.KeyRotation?.RequiresCertificate ?? false;

    /// <summary>
    /// 当前是否可执行更换密钥
    /// </summary>
    public bool CanRotateKey => _entry is { Device.IsConnected: true, KeyRotation: not null };

    /// <summary>
    /// 最近一次更换密钥的结果
    /// </summary>
    [ObservableProperty]
    public partial string? KeyInfoText { get; set; }

    /// <summary>
    /// 重新读取当前选择并同步连接状态
    /// </summary>
    public void Refresh()
    {
        SetEntry(_manager?.SelectedDevice);
    }

    /// <summary>
    /// 连接当前设备
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_entry is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("连接设备 {Device:l}", _entry.Name);

        try
        {
            await _entry.Device.ConnectAsync();
            Logger.Information("设备 {Device:l} 已连接", _entry.Name);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "连接设备 {Device:l} 失败", _entry.Name);
            ErrorMessage = exception.Message;
        }
        finally
        {
            NotifyConnectionStateChanged();
        }
    }

    private bool CanConnect() => _entry is { Device.IsConnected: false };

    /// <summary>
    /// 断开当前设备
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (_entry is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("断开设备 {Device:l}", _entry.Name);

        try
        {
            await _entry.Device.DisconnectAsync();
            Logger.Information("设备 {Device:l} 已断开", _entry.Name);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "断开设备 {Device:l} 失败", _entry.Name);
            ErrorMessage = exception.Message;
        }
        finally
        {
            NotifyConnectionStateChanged();
        }
    }

    private bool CanDisconnect() => _entry is { Device.IsConnected: true };

    /// <summary>
    /// 设置设备工作状态
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSetStatus))]
    private async Task SetStatusAsync()
    {
        if (_entry is null || SelectedStatus is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("设置设备 {Device:l} 状态为 {Status:l}", _entry.Name,
            SelectedStatus.DisplayName);

        try
        {
            await _entry.Device.SetStatusAsync(SelectedStatus.Value);
            Logger.Information("设备 {Device:l} 状态已设置为 {Status:l}", _entry.Name,
                SelectedStatus.DisplayName);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "设置设备 {Device:l} 状态失败", _entry.Name);
            ErrorMessage = exception.Message;
        }
    }

    private bool CanSetStatus() => _entry is { Device.IsConnected: true } && SelectedStatus is not null;

    /// <summary>
    /// 更换当前设备的密钥，供界面在选定证书后调用
    /// </summary>
    /// <param name="certificateDer">
    /// 新的 DKL 证书（DER）；<see cref="RequiresKeyCertificate"/> 为 false 时可传 <c>null</c>
    /// </param>
    public async Task RotateDeviceKeyAsync(ReadOnlyMemory<byte>? certificateDer = null)
    {
        var entry = _entry;
        var keyRotation = entry?.KeyRotation;
        if (entry is null || keyRotation is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("更换设备 {Device:l} 的密钥", entry.Name);

        try
        {
            var info = await keyRotation.RotateKeyAsync(certificateDer);
            KeyInfoText = DescribeKey(info);
            Logger.Information("设备 {Device:l} 密钥已更换，代数 {RotationId}，指纹 {Fingerprint:l}",
                entry.Name, info.RotationId, info.Fingerprint);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "更换设备 {Device:l} 密钥失败", entry.Name);
            ErrorMessage = exception.Message;
        }
    }

    /// <summary>
    /// 把设备密钥信息格式化为界面文本
    /// </summary>
    private static string DescribeKey(DeviceKeyInfo info)
    {
        var fingerprint = info.Fingerprint is { Length: > 16 } value
            ? $"{value[..16]}…"
            : info.Fingerprint;

        return string.IsNullOrEmpty(fingerprint)
            ? $"密钥代数 {info.RotationId}"
            : $"密钥代数 {info.RotationId} · 指纹 {fingerprint}";
    }

    /// <summary>
    /// 刷新命令
    /// </summary>
    [RelayCommand]
    private void RefreshDevice() => Refresh();

    partial void OnSelectedStatusChanged(DeviceStatusOption? value)
    {
        _ = value;
        SetStatusCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedDeviceChanged(object? sender, EventArgs e)
    {
        SetEntry(_manager?.SelectedDevice);
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        NotifyConnectionStateChanged();
    }

    private void SetEntry(SbiDeviceEntry? entry)
    {
        _entry = entry;
        ErrorMessage = null;
        KeyInfoText = null;
        Logger.Debug("当前操作设备切换为 {Device:l}", entry?.Name ?? "无");

        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(RoleText));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(HasKeyRotation));
        OnPropertyChanged(nameof(RequiresKeyCertificate));

        NotifyConnectionStateChanged();
    }

    private void NotifyConnectionStateChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(CanRotateKey));

        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        SetStatusCommand.NotifyCanExecuteChanged();
    }
}
