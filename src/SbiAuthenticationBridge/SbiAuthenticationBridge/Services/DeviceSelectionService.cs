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

    private ISbiDevice? _selectedDevice;

    /// <inheritdoc />
    public ISbiDevice? SelectedDevice
    {
        get
        {
            lock (_gate)
            {
                return _selectedDevice;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<DeviceSelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc />
    public void Select(ISbiDevice? device)
    {
        ISbiDevice? previousDevice;

        lock (_gate)
        {
            if (ReferenceEquals(_selectedDevice, device))
            {
                return;
            }

            previousDevice = _selectedDevice;
            _selectedDevice = device;
        }

        SelectionChanged?.Invoke(this, new DeviceSelectionChangedEventArgs(previousDevice, device));
    }
}
