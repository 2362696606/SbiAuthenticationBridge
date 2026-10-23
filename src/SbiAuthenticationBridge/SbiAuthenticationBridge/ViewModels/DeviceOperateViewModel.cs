using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Core.Security;
using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 单设备操作视图模型；当前设备由全局设备选择决定
/// </summary>
public partial class DeviceOperateViewModel : ViewModelBase, IDisposable
{
    private readonly IDeviceSelectionService _selectionService;

    private readonly IDkl1CertificateIssuer _certificateIssuer;

    /// <summary>
    /// 构造期间不触发选择变化处理
    /// </summary>
    private readonly bool _isInitializing = true;

    /// <summary>
    /// 创建设备操作视图模型
    /// </summary>
    /// <param name="selectionService">全局设备选择服务</param>
    /// <param name="certificateIssuer">DKL1 叶证书签发器</param>
    public DeviceOperateViewModel(
        IDeviceSelectionService selectionService,
        IDkl1CertificateIssuer certificateIssuer)
    {
        ArgumentNullException.ThrowIfNull(selectionService);
        ArgumentNullException.ThrowIfNull(certificateIssuer);

        _selectionService = selectionService;
        _certificateIssuer = certificateIssuer;
        KeySubjectDescription = certificateIssuer.SubjectDescription;
        IsKeyRotationConfigured = certificateIssuer.IsConfigured;

        SelectedDevice = selectionService.SelectedDevice;
        SelectedDeviceId = selectionService.DeviceId;
        selectionService.SelectionChanged += OnSelectionChanged;
        _isInitializing = false;

        IsConnected = SelectedDevice is IDevice { IsConnected: true };
        SelectedStatusOption = StatusOptions[0];
    }

    /// <summary>
    /// 可设置的设备状态
    /// </summary>
    public IReadOnlyList<DeviceStatusOption> StatusOptions { get; } =
    [
        new(DeviceStatus.Ready, "就绪"),
        new(DeviceStatus.Busy, "忙"),
        new(DeviceStatus.NotReady, "未就绪"),
        new(DeviceStatus.NotRegistered, "未注册")
    ];

    /// <summary>
    /// 当前选择的设备；未选择时为 <see langword="null"/>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDevice))]
    public partial ISbiDevice? SelectedDevice { get; set; }

    /// <summary>
    /// 当前选择设备的内部标识；未选择时为 <see langword="null"/>
    /// </summary>
    [ObservableProperty]
    public partial string? SelectedDeviceId { get; set; }

    /// <summary>
    /// 是否已选择设备
    /// </summary>
    public bool HasSelectedDevice => SelectedDevice is not null;

    /// <summary>
    /// 叶证书 Subject，用于界面展示
    /// </summary>
    public string KeySubjectDescription { get; }

    /// <summary>
    /// 是否已配置 DKL1 签发材料
    /// </summary>
    public bool IsKeyRotationConfigured { get; }

    /// <summary>
    /// 是否正在执行操作
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// 设备是否已连接
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial bool IsConnected { get; set; }

    /// <summary>
    /// 最近一次错误
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 设备当前状态
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStatusText))]
    public partial DeviceStatus? CurrentStatus { get; set; }

    /// <summary>
    /// 待设置的状态
    /// </summary>
    [ObservableProperty]
    public partial DeviceStatusOption? SelectedStatusOption { get; set; }

    /// <summary>
    /// 是否有错误信息
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// 设备是否提供物理连接状态
    /// </summary>
    public bool HasConnectionState => SelectedDevice is IDevice;

    /// <summary>
    /// 连接状态文本
    /// </summary>
    public string ConnectionText => !HasConnectionState
        ? "不适用"
        : IsConnected
            ? "已连接"
            : "未连接";

    /// <summary>
    /// 当前状态文本
    /// </summary>
    public string CurrentStatusText => CurrentStatus switch
    {
        DeviceStatus.Ready => "就绪",
        DeviceStatus.Busy => "忙",
        DeviceStatus.NotReady => "未就绪",
        DeviceStatus.NotRegistered => "未注册",
        _ => "未知"
    };

    /// <summary>
    /// 当前设备类型描述
    /// </summary>
    public string DeviceSummary => SelectedDevice switch
    {
        null => "未选择设备",
        ISbiAuthenticationDevice => "认证设备",
        ISbiRegistrationDevice => "注册设备",
        _ => "设备服务"
    };

    partial void OnSelectedDeviceChanged(ISbiDevice? value)
    {
        if (_isInitializing)
        {
            return;
        }

        HandleSelectedDeviceChanged(value);
    }

    /// <summary>
    /// 当前设备变化后刷新与该设备相关的状态
    /// </summary>
    private void HandleSelectedDeviceChanged(ISbiDevice? device)
    {
        CurrentStatus = null;
        ErrorMessage = null;
        IsConnected = device is IDevice { IsConnected: true };
        SelectedStatusOption = StatusOptions[0];

        OnPropertyChanged(nameof(DeviceSummary));
        OnPropertyChanged(nameof(HasConnectionState));
        OnPropertyChanged(nameof(ConnectionText));
        NotifyCommandStates();

        _ = RefreshStateAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnSelectedStatusOptionChanged(DeviceStatusOption? value)
    {
        _ = value;
        SetStatusCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 刷新连接状态与设备状态
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshStateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await LoadDeviceStateAsync(device);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var selectedDevice = SelectedDevice;
        var selectedDeviceId = SelectedDeviceId;

        if (selectedDevice is not IDevice device)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await device.ConnectAsync();

            if (IsStillSelected(selectedDeviceId))
            {
                IsConnected = device.IsConnected;
                await LoadDeviceStateAsync(selectedDevice);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (SelectedDevice is not IDevice device)
        {
            return;
        }

        var selectedDeviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await device.DisconnectAsync();

            if (IsStillSelected(selectedDeviceId))
            {
                IsConnected = device.IsConnected;
                CurrentStatus = DeviceStatus.NotReady;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetStatus))]
    private async Task SetStatusAsync()
    {
        if (SelectedDevice is not IManualDeviceStatus statusControl
            || SelectedStatusOption is not { } statusOption)
        {
            return;
        }

        var selectedDeviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await statusControl.SetDeviceStatusAsync(statusOption.Value);

            if (IsStillSelected(selectedDeviceId))
            {
                CurrentStatus = statusOption.Value;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRotateKey))]
    private async Task RotateKeyAsync()
    {
        if (SelectedDevice is not IFtmSecureDevice secureDevice)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var publicKey = await secureDevice.GetDkl1PublicKeyAsync();
            var certificateDer = _certificateIssuer.Issue(publicKey.SubjectPublicKeyInfo);
            await secureDevice.SetDkl1CertificateAsync(certificateDer);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy && HasSelectedDevice;

    private bool CanConnect() => !IsBusy && SelectedDevice is IDevice { IsConnected: false };

    private bool CanDisconnect() => !IsBusy && SelectedDevice is IDevice { IsConnected: true };

    private bool CanSetStatus() =>
        !IsBusy
        && IsDeviceUsable
        && SelectedStatusOption is not null
        && SelectedDevice is IManualDeviceStatus;

    private bool CanRotateKey() =>
        !IsBusy
        && IsDeviceUsable
        && IsKeyRotationConfigured
        && SelectedDevice is IFtmSecureDevice;

    /// <summary>
    /// 对不提供物理连接状态的服务型设备，视为可用
    /// </summary>
    private bool IsDeviceUsable =>
        SelectedDevice is not IDevice physicalDevice || physicalDevice.IsConnected;

    private async Task LoadDeviceStateAsync(ISbiDevice device)
    {
        var deviceId = SelectedDeviceId;

        if (device is IDevice physicalDevice)
        {
            IsConnected = physicalDevice.IsConnected;
        }

        if (device is not IManualDeviceStatus statusControl)
        {
            return;
        }

        var status = await statusControl.GetDeviceStatusAsync();
        if (!IsStillSelected(deviceId))
        {
            return;
        }

        CurrentStatus = status;

        foreach (var option in StatusOptions)
        {
            if (option.Value == status)
            {
                SelectedStatusOption = option;
                break;
            }
        }
    }

    private void NotifyCommandStates()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshStateCommand.NotifyCanExecuteChanged();
        SetStatusCommand.NotifyCanExecuteChanged();
        RotateKeyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 异步操作开始后选择是否仍停留在同一设备；按内部标识判断，不依赖实例引用
    /// </summary>
    private bool IsStillSelected(string? deviceId) =>
        string.Equals(SelectedDeviceId, deviceId, StringComparison.Ordinal);

    private void OnSelectionChanged(object? sender, DeviceSelectionChangedEventArgs e)
    {
        var deviceChanged = !ReferenceEquals(SelectedDevice, e.SelectedDevice);
        var deviceIdChanged = !string.Equals(SelectedDeviceId, e.SelectedDeviceId,
            StringComparison.Ordinal);

        SelectedDevice = e.SelectedDevice;
        SelectedDeviceId = e.SelectedDeviceId;

        // 实例未变但内部标识变化时 SelectedDevice 不会触发属性变更，需显式刷新
        if (!deviceChanged && deviceIdChanged)
        {
            HandleSelectedDeviceChanged(SelectedDevice);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnSelectionChanged;
        GC.SuppressFinalize(this);
    }

}