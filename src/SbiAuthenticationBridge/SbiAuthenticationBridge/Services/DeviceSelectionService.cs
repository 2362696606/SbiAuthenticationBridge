using System;
using System.Threading;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Services;

/// <summary>
/// <see cref="IDeviceSelectionService"/> 的默认实现
/// </summary>
public sealed class DeviceSelectionService : IDeviceSelectionService
{
    private readonly Lock _gate = new();

    /// <summary>
    /// 当前选择；内部标识与设备实例始终一起更新，避免读到不一致的组合
    /// </summary>
    private SelectionEntry _current;

    /// <inheritdoc />
    public ISbiDevice? SelectedDevice => ReadSelection().Device;

    /// <inheritdoc />
    public string? DeviceId => ReadSelection().Id;

    /// <inheritdoc />
    public event EventHandler<DeviceSelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc />
    public void Select(string? deviceId, ISbiDevice? device)
    {
        if (device is null)
        {
            // 取消选择时内部标识必须一起清空，避免残留过期 ID
            deviceId = null;
        }
        else
        {
            ArgumentException.ThrowIfNullOrEmpty(deviceId);
        }

        DeviceSelectionChangedEventArgs args;

        lock (_gate)
        {
            // 内部标识与实例任一变化都视为选择变化
            if (string.Equals(_current.Id, deviceId, StringComparison.Ordinal)
                && ReferenceEquals(_current.Device, device))
            {
                return;
            }

            var previous = _current;
            _current = new SelectionEntry(deviceId, device);

            args = new DeviceSelectionChangedEventArgs(previous.Id, previous.Device, deviceId, device);
        }

        SelectionChanged?.Invoke(this, args);
    }

    private SelectionEntry ReadSelection()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    private readonly record struct SelectionEntry(string? Id, ISbiDevice? Device);
}