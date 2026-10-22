using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备枚举视图模型：枚举 <see cref="ISbiDeviceProvider"/> 中的设备并维护全局设备选择
/// </summary>
public partial class DevicesViewModel : ViewModelBase, IDisposable
{
    private readonly ISbiDeviceProvider _deviceProvider;
    private readonly IDeviceSelectionService _selectionService;

    /// <summary>
    /// 防止视图模型与服务之间的选择同步互相触发
    /// </summary>
    private bool _syncingSelection;

    /// <summary>
    /// 创建设备枚举视图模型
    /// </summary>
    /// <param name="deviceProvider">设备提供者</param>
    /// <param name="selectionService">全局设备选择服务</param>
    public DevicesViewModel(ISbiDeviceProvider deviceProvider, IDeviceSelectionService selectionService)
    {
        ArgumentNullException.ThrowIfNull(deviceProvider);
        ArgumentNullException.ThrowIfNull(selectionService);

        _deviceProvider = deviceProvider;
        _selectionService = selectionService;
        _selectionService.SelectionChanged += OnGlobalSelectionChanged;
    }

    /// <summary>
    /// 可枚举的设备
    /// </summary>
    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    /// <summary>
    /// 当前选择的设备
    /// </summary>
    [ObservableProperty]
    public partial DeviceItemViewModel? SelectedDevice { get; set; }

    /// <summary>
    /// 是否正在刷新
    /// </summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    /// <summary>
    /// 是否存在可枚举的设备
    /// </summary>
    public bool HasDevices => Devices.Count > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnGlobalSelectionChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 重新枚举设备并刷新各设备的基础信息与状态
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;

        try
        {
            SynchronizeDeviceItems();

            foreach (var device in Devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await device.RefreshAsync(cancellationToken);
            }

            RestoreSelection();
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(HasDevices));
        }
    }

    /// <summary>
    /// 让列表与设备提供者保持一致，尽量复用已有列表项以免打断选择
    /// </summary>
    private void SynchronizeDeviceItems()
    {
        var providedDevices = _deviceProvider.Devices;

        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            if (!providedDevices.Any(device => ReferenceEquals(device, Devices[index].Device)))
            {
                Devices.RemoveAt(index);
            }
        }

        foreach (var device in providedDevices)
        {
            if (Devices.Any(item => ReferenceEquals(item.Device, device)))
            {
                continue;
            }

            Devices.Add(new DeviceItemViewModel(device, Devices.Count + 1));
        }
    }

    /// <summary>
    /// 把全局选择映射回列表项；设备已不存在时清除选择
    /// </summary>
    private void RestoreSelection()
    {
        var selectedItem = Devices.FirstOrDefault(item =>
            ReferenceEquals(item.Device, _selectionService.SelectedDevice));

        if (ReferenceEquals(SelectedDevice, selectedItem))
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            SelectedDevice = selectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }

        if (selectedItem is null && _selectionService.SelectedDevice is not null)
        {
            _selectionService.Select(null);
        }
    }

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        if (_syncingSelection)
        {
            return;
        }

        _selectionService.Select(value?.Device);
    }

    private void OnGlobalSelectionChanged(object? sender, DeviceSelectionChangedEventArgs e)
    {
        if (ReferenceEquals(SelectedDevice?.Device, e.SelectedDevice))
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            SelectedDevice = Devices.FirstOrDefault(item =>
                ReferenceEquals(item.Device, e.SelectedDevice));
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}