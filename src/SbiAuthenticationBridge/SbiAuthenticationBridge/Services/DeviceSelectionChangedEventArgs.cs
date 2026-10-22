using System;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Services;

/// <summary>
/// 全局设备选择变化事件参数
/// </summary>
public sealed class DeviceSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建事件参数
    /// </summary>
    /// <param name="previousDevice">变化前选择的设备</param>
    /// <param name="selectedDevice">变化后选择的设备</param>
    public DeviceSelectionChangedEventArgs(ISbiDevice? previousDevice, ISbiDevice? selectedDevice)
    {
        PreviousDevice = previousDevice;
        SelectedDevice = selectedDevice;
    }

    /// <summary>
    /// 变化前选择的设备
    /// </summary>
    public ISbiDevice? PreviousDevice { get; }

    /// <summary>
    /// 变化后选择的设备
    /// </summary>
    public ISbiDevice? SelectedDevice { get; }
}
