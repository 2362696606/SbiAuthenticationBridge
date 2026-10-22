using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 作用于单个设备的视图模型基类：设备选择与全局选择保持同步
/// </summary>
public abstract partial class DeviceScopedViewModel : ViewModelBase, IDisposable
{
    private readonly IDeviceSelectionService _selectionService;

    /// <summary>
    /// 创建设备视图模型
    /// </summary>
    /// <param name="selectionService">全局设备选择服务</param>
    protected DeviceScopedViewModel(IDeviceSelectionService selectionService)
    {
        ArgumentNullException.ThrowIfNull(selectionService);

        _selectionService = selectionService;
        SelectedDevice = selectionService.SelectedDevice;
        selectionService.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>
    /// 当前选择的设备；未选择时为 <see langword="null"/>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDevice))]
    public partial ISbiDevice? SelectedDevice { get; set; }

    /// <summary>
    /// 是否已选择设备
    /// </summary>
    public bool HasSelectedDevice => SelectedDevice is not null;

    /// <inheritdoc />
    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnSelectionChanged;
        GC.SuppressFinalize(this);
    }

    private void OnSelectionChanged(object? sender, DeviceSelectionChangedEventArgs e)
    {
        SelectedDevice = e.SelectedDevice;
    }
}
