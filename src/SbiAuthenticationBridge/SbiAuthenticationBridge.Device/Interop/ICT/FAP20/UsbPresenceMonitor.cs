using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

/// <summary>
/// 按 VID/PID 轮询 USB 设备的物理在线状态。
/// </summary>
/// <remarks>
/// 句柄非空只说明设备曾经成功打开，不能说明 USB 仍然连接。
/// 本类通过 SetupAPI 枚举当前存在的设备，在插入/拔出时更新状态并触发事件。
/// </remarks>
internal sealed class UsbPresenceMonitor : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ushort _vendorId;

    private readonly ushort _productId;

    private readonly CancellationTokenSource _cts = new();

    private Task? _loop;

    private volatile bool _isPresent;

    private bool _disposed;

    internal UsbPresenceMonitor(ushort vendorId, ushort productId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("FAP20 的 USB 物理在线监测目前只支持 Windows。");
        }

        if (vendorId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vendorId), "USB vendorId 不能为 0。");
        }

        if (productId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId), "USB productId 不能为 0。");
        }

        _vendorId = vendorId;
        _productId = productId;
    }

    internal bool IsPresent => _isPresent;

    internal event EventHandler<bool>? PresenceChanged;

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loop is not null)
        {
            return;
        }

        var initialPresent = Probe();
        _isPresent = initialPresent;
        _loop = Task.Run(() => RunAsync(initialPresent, _cts.Token));
    }

    /// <summary>
    /// 立即探测一次物理在线状态，供连接前确认使用。
    /// </summary>
    internal bool Refresh()
    {
        var present = Probe();
        _isPresent = present;
        return present;
    }

    private bool Probe()
    {
        try
        {
            return WindowsUsbDeviceEnumerator.IsPresent(_vendorId, _productId);
        }
        catch (Exception exception)
        {
            // 枚举失败不等于设备已拔出，保持上一次状态
            Debug.WriteLine($"[FAP20] USB 物理在线探测失败：{exception}");
            return _isPresent;
        }
    }

    private async Task RunAsync(bool initialPresent, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        var last = initialPresent;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var present = Probe();
                if (present == last)
                {
                    continue;
                }

                last = present;
                _isPresent = present;

                try
                {
                    PresenceChanged?.Invoke(this, present);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"[FAP20] USB 在线状态订阅方抛出异常：{exception}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[FAP20] USB 在线监测停止失败：{exception}");
            }
        }

        _cts.Dispose();
    }
}

/// <summary>
/// 使用 SetupAPI 按 VID/PID 枚举当前存在的 USB 设备。
/// </summary>
internal static partial class WindowsUsbDeviceEnumerator
{
    private const uint DigcfPresent = 0x00000002;

    private const uint DigcfAllClasses = 0x00000004;

    private const uint SpdrpHardwareId = 0x00000001;

    private const int ErrorNoMoreItems = 259;

    internal static bool IsPresent(ushort vendorId, ushort productId)
    {
        var hardwareIdPrefix = $"USB\\VID_{vendorId:X4}&PID_{productId:X4}";
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero, "USB", IntPtr.Zero, DigcfPresent | DigcfAllClasses);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "枚举 USB 设备失败。");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var deviceInfo = new SpDevInfoData
                {
                    cbSize = (uint)Marshal.SizeOf<SpDevInfoData>()
                };

                if (NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo) == 0)
                {
                    var error = Marshal.GetLastWin32Error();

                    if (error != ErrorNoMoreItems)
                    {
                        throw new Win32Exception(error, "枚举 USB 设备节点失败。");
                    }

                    return false;
                }

                if (HasHardwareId(deviceInfoSet, ref deviceInfo, hardwareIdPrefix))
                {
                    return true;
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool HasHardwareId(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfo,
        string hardwareIdPrefix)
    {
        NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfo, SpdrpHardwareId,
            out _, IntPtr.Zero, 0, out var requiredSize);

        if (requiredSize == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)requiredSize);

        try
        {
            if (NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfo, SpdrpHardwareId,
                    out _, buffer, requiredSize, out _) == 0)
            {
                return false;
            }

            var bytes = new byte[requiredSize];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            var text = Encoding.Unicode.GetString(bytes);

            return text.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Any(hardwareId => hardwareId.StartsWith(hardwareIdPrefix, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        internal uint cbSize;
        internal Guid ClassGuid;
        internal uint DevInst;
        internal IntPtr Reserved;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW",
            StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator,
            IntPtr hwndParent, uint flags);

        [LibraryImport("setupapi.dll", SetLastError = true)]
        internal static partial int SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex,
            ref SpDevInfoData deviceInfoData);

        [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
        internal static partial int SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData, uint property, out uint propertyRegDataType,
            IntPtr propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [LibraryImport("setupapi.dll", SetLastError = true)]
        internal static partial void SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}
