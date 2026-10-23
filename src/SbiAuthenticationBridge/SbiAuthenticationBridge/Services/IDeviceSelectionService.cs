using System;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Services;

/// <summary>
/// 全局设备选择：所有针对单个设备的视图共享同一个选择结果
/// </summary>
/// <remarks>
/// 设备枚举视图负责写入选择结果，其他设备操作视图订阅
/// <see cref="SelectionChanged"/> 或读取 <see cref="SelectedDevice"/> 保持同步。
/// </remarks>
public interface IDeviceSelectionService
{
    /// <summary>
    /// 当前选择的设备；未选择时为 <see langword="null"/>
    /// </summary>
    ISbiDevice? SelectedDevice { get; }

    /// <summary>
    /// 当前选择设备的内部标识；未选择时为 <see langword="null"/>
    /// </summary>
    string? DeviceId { get; }

    /// <summary>
    /// 选择发生变化时触发
    /// </summary>
    event EventHandler<DeviceSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// 设置当前选择的设备
    /// </summary>
    /// <param name="deviceId">设备Id</param>
    /// <param name="device">要选择的设备；<see langword="null"/> 表示取消选择</param>
    void Select(string? deviceId, ISbiDevice? device);
}
