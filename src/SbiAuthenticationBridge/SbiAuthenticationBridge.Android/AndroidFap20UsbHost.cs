using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using AndroidX.Core.Content;
using SbiAuthenticationBridge.Device;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// Android USB 宿主：完成 UsbManager 授权、ClaimInterface，并把文件描述符交给 ftmBridgeAPI
/// </summary>
/// <remarks>
/// 非 root 应用无法枚举 <c>/dev/bus/usb</c>，因此 Android 上必须走
/// <c>UsbManager</c> + <c>ftm_open_fd()</c>，不能使用 <c>ftm_open()</c>。
/// </remarks>
internal sealed class AndroidFap20UsbHost : IFap20UsbHost
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Android");

    private static readonly TimeSpan PermissionTimeout = TimeSpan.FromSeconds(60);

    private readonly Context _context;

    private readonly UsbManager _usbManager;

    internal AndroidFap20UsbHost(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _usbManager = context.GetSystemService(Context.UsbService) as UsbManager
                      ?? throw new InvalidOperationException("当前设备不支持 USB Host。");
    }

    /// <inheritdoc />
    public async Task<IFap20UsbSession?> OpenAsync(ushort vendorId, ushort productId,
        CancellationToken cancellationToken = default)
    {
        var device = FindDevice(vendorId, productId);
        if (device is null)
        {
            Logger.Warning("未找到 VID=0x{VendorId:X4} PID=0x{ProductId:X4} 的 USB 设备",
                vendorId, productId);
            return null;
        }

        if (!await EnsurePermissionAsync(device, cancellationToken).ConfigureAwait(false))
        {
            Logger.Warning("未获得 USB 设备访问权限");
            return null;
        }

        var connection = _usbManager.OpenDevice(device);
        if (connection is null)
        {
            Logger.Error("UsbManager.OpenDevice 返回空");
            return null;
        }

        var claimedInterfaces = new List<UsbInterface>();

        try
        {
            // CDC 控制接口（0）由内核 cdc_acm 占用；先 force claim 使其脱管，
            // 再声明真正的批量数据接口（通常是 1，端点 0x82/0x01）。
            var controlInterface = device.GetInterface(0);
            if (controlInterface is not null && connection.ClaimInterface(controlInterface, true))
            {
                claimedInterfaces.Add(controlInterface);
            }
            else
            {
                Logger.Warning("ClaimInterface(0) 失败，继续尝试批量数据接口");
            }

            var dataInterface = FindBulkInterface(device)
                                ?? throw new InvalidOperationException(
                                    "未找到 FAP20 的批量传输接口，请确认设备描述符。");

            if (!claimedInterfaces.Contains(dataInterface))
            {
                if (!connection.ClaimInterface(dataInterface, true))
                {
                    throw new InvalidOperationException(
                        $"ClaimInterface({dataInterface.Id}) 失败，USB 接口可能已被其他应用占用。");
                }

                claimedInterfaces.Add(dataInterface);
            }

            var fileDescriptor = connection.FileDescriptor;
            var handle = Fap20AndroidSdk.OpenFromFileDescriptor(fileDescriptor);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "ftm_open_fd 失败，请检查 FingerPrintDebug.INI 中的端点配置。");
            }

            Logger.Information(
                "FAP20 USB 设备已打开，fd={FileDescriptor}，已声明接口 {Interfaces}",
                fileDescriptor, string.Join(",", claimedInterfaces.Select(item => item.Id)));
            return new AndroidFap20UsbSession(handle, connection, claimedInterfaces);
        }
        catch
        {
            foreach (var usbInterface in Enumerable.Reverse(claimedInterfaces))
            {
                connection.ReleaseInterface(usbInterface);
            }

            connection.Close();
            throw;
        }
    }

    /// <summary>
    /// 查找同时包含批量 IN 与批量 OUT 端点的接口
    /// </summary>
    /// <remarks>
    /// FAP20 的批量端点由 <c>FingerPrintDebug.INI</c> 指定（默认 0x82 接收、0x01 发送），
    /// 它们位于 CDC Data 接口（接口 1），不是内核 cdc_acm 使用的接口 0。
    /// </remarks>
    private static UsbInterface? FindBulkInterface(UsbDevice device)
    {
        for (var interfaceIndex = 0; interfaceIndex < device.InterfaceCount; interfaceIndex++)
        {
            var candidate = device.GetInterface(interfaceIndex);
            if (candidate is null)
            {
                continue;
            }

            var hasBulkIn = false;
            var hasBulkOut = false;

            for (var endpointIndex = 0; endpointIndex < candidate.EndpointCount; endpointIndex++)
            {
                var endpoint = candidate.GetEndpoint(endpointIndex);
                if (endpoint is null || endpoint.Type != UsbAddressing.XferBulk)
                {
                    continue;
                }

                if (((int)endpoint.Address & 0x80) != 0)
                {
                    hasBulkIn = true;
                }
                else
                {
                    hasBulkOut = true;
                }
            }

            if (hasBulkIn && hasBulkOut)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 按 VID/PID 查找已插入的设备
    /// </summary>
    private UsbDevice? FindDevice(ushort vendorId, ushort productId)
    {
        return _usbManager.DeviceList?.Values.FirstOrDefault(device =>
            device.VendorId == vendorId && device.ProductId == productId);
    }

    /// <summary>
    /// 确保应用已获得该 USB 设备的访问权限
    /// </summary>
    private async Task<bool> EnsurePermissionAsync(UsbDevice device,
        CancellationToken cancellationToken)
    {
        if (_usbManager.HasPermission(device))
        {
            return true;
        }

        var packageName = _context.PackageName;
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException("无法取得应用包名。");
        }

        var action = $"{packageName}.USB_PERMISSION";
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new UsbPermissionReceiver(completion);

        // USB 授权结果由系统广播；使用 NotExported 会收不到，导致首次调用误判为拒绝。
        ContextCompat.RegisterReceiver(_context, receiver, new IntentFilter(action),
            ContextCompat.ReceiverExported);

        try
        {
            var intent = new Intent(action).SetPackage(packageName);
            var pendingIntent = PendingIntent.GetBroadcast(_context, 0, intent,
                PendingIntentFlags.Immutable)
                                ?? throw new InvalidOperationException("无法创建 USB 授权 PendingIntent。");

            _usbManager.RequestPermission(device, pendingIntent);

            return await completion.Task.WaitAsync(PermissionTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Logger.Warning("等待 USB 授权超时");
            return false;
        }
        finally
        {
            _context.UnregisterReceiver(receiver);
        }
    }

    /// <summary>
    /// 接收 UsbManager 的授权结果
    /// </summary>
    private sealed class UsbPermissionReceiver : BroadcastReceiver
    {
        private readonly TaskCompletionSource<bool> _completion;

        internal UsbPermissionReceiver(TaskCompletionSource<bool> completion)
        {
            _completion = completion;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            var granted = intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false;
            _completion.TrySetResult(granted);
        }
    }

    /// <summary>
    /// 关闭 UsbDeviceConnection 的会话；SDK 句柄已由 <see cref="Device.FAP20"/> 先行关闭
    /// </summary>
    private sealed class AndroidFap20UsbSession : IFap20UsbSession
    {
        private readonly UsbDeviceConnection _connection;

        private readonly IReadOnlyList<UsbInterface> _usbInterfaces;

        private bool _disposed;

        internal AndroidFap20UsbSession(IntPtr handle, UsbDeviceConnection connection,
            IReadOnlyList<UsbInterface> usbInterfaces)
        {
            Handle = handle;
            _connection = connection;
            _usbInterfaces = usbInterfaces;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var usbInterface in _usbInterfaces.Reverse())
            {
                try
                {
                    _connection.ReleaseInterface(usbInterface);
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, "释放 USB 接口失败");
                }
            }

            try
            {
                _connection.Close();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "关闭 USB 连接失败");
            }
        }
    }
}
