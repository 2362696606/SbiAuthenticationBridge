using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

namespace SbiAuthenticationBridge.Device.Android;

/// <summary>
/// Android 实现：UsbManager 申请权限，按文件描述符交给 <c>ftm_open_fd</c>
/// </summary>
internal sealed class AndroidFtmDeviceOpener : IFtmDeviceOpener
{
    /// <summary>
    /// 等待用户授权的上限
    /// </summary>
    private const int PermissionTimeoutMs = 15_000;

    /// <summary>
    /// USB 授权广播 action 后缀
    /// </summary>
    private const string PermissionActionSuffix = ".USB_PERMISSION";

    private readonly UsbManager _usbManager;

    private readonly Context _context;

    private readonly ushort _vendorId;

    private readonly ushort _productId;

    private readonly ILogger _logger;

    private readonly string _permissionAction;

    private readonly Lock _gate = new();

    private BroadcastReceiver? _detachReceiver;

    private UsbDeviceConnection? _connection;

    private UsbInterface? _interface;

    private bool _started;

    private bool _disposed;

    private volatile bool _isPresent;

    /// <summary>
    /// 创建 Android 设备打开器
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID</param>
    /// <param name="productId">USB 产品 ID</param>
    /// <param name="logger">日志</param>
    public AndroidFtmDeviceOpener(ushort vendorId, ushort productId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _vendorId = vendorId;
        _productId = productId;
        _logger = logger;
        _context = global::Android.App.Application.Context;
        _usbManager = (UsbManager?)_context.GetSystemService(Context.UsbService)
            ?? throw new InvalidOperationException("当前设备不支持 USB Host。");
        _permissionAction = _context.PackageName + PermissionActionSuffix;
    }

    /// <inheritdoc />
    public bool IsPresent => _isPresent;

    /// <inheritdoc />
    public string LogFilePath => Path.Combine(_context.FilesDir!.AbsolutePath, "sdk_runtime.log");

    /// <inheritdoc />
    public event EventHandler<bool>? PresenceChanged;

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        _started = true;
        _detachReceiver = new UsbDetachReceiver(_vendorId, _productId, OnDeviceDetached);
        RegisterReceiver(_detachReceiver, new IntentFilter(UsbManager.ActionUsbDeviceDetached));
        _isPresent = FindDevice() is not null;
    }

    /// <inheritdoc />
    public bool Refresh()
    {
        _isPresent = FindDevice() is not null;
        return _isPresent;
    }

    /// <inheritdoc />
    public async Task<IntPtr> OpenAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var device = FindDevice()
            ?? throw new InvalidOperationException("未找到 FAP20 设备（USB 未插入或 VID/PID 不匹配）。");

        if (!_usbManager.HasPermission(device))
        {
            var granted = await RequestPermissionAsync(device).ConfigureAwait(false);
            if (!granted)
            {
                throw new InvalidOperationException("用户未授予 FAP20 的 USB 访问权限。");
            }
        }

        UsbDeviceConnection? connection = null;
        UsbInterface? usbInterface = null;

        try
        {
            connection = _usbManager.OpenDevice(device)
                ?? throw new InvalidOperationException("打开 FAP20 USB 设备失败（OpenDevice 返回 null）。");

            usbInterface = device.GetInterface(0)
                ?? throw new InvalidOperationException("FAP20 USB 接口 0 不存在。");

            if (!connection.ClaimInterface(usbInterface, force: true))
            {
                throw new InvalidOperationException("申请 FAP20 USB 接口失败（ClaimInterface）。");
            }

            FtmDeviceNative.SetLogPath(LogFilePath, FtmLogLevel.Warn);
            var handle = FtmDeviceNative.OpenFd(connection.FileDescriptor);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("ftm_open_fd 失败，无法初始化 FAP20。");
            }

            lock (_gate)
            {
                ReleaseConnection();
                _connection = connection;
                _interface = usbInterface;
            }

            _logger.LogInformation(
                "FAP20 已通过 USB 打开，fd={FileDescriptor}",
                connection.FileDescriptor);
            return handle;
        }
        catch
        {
            if (connection is not null)
            {
                if (usbInterface is not null)
                {
                    connection.ReleaseInterface(usbInterface);
                }

                connection.Close();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return default;
        }

        _disposed = true;

        if (_detachReceiver is not null)
        {
            try
            {
                _context.UnregisterReceiver(_detachReceiver);
            }
            catch (ArgumentException)
            {
                // 未注册成功时忽略
            }

            _detachReceiver = null;
        }

        lock (_gate)
        {
            ReleaseConnection();
        }

        return default;
    }

    /// <summary>
    /// 按 VID/PID 查找当前连接的设备
    /// </summary>
    private UsbDevice? FindDevice()
    {
        var devices = _usbManager.DeviceList;
        if (devices is null)
        {
            return null;
        }

        foreach (var device in devices.Values)
        {
            if (device.VendorId == _vendorId && device.ProductId == _productId)
            {
                return device;
            }
        }

        return null;
    }

    private void OnDeviceDetached()
    {
        _isPresent = false;
        _logger.LogWarning("FAP20 USB 设备已拔出");
        PresenceChanged?.Invoke(this, false);
    }

    /// <summary>
    /// 申请 USB 访问权限并等待用户操作
    /// </summary>
    private async Task<bool> RequestPermissionAsync(UsbDevice device)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new UsbPermissionReceiver(completion);
        RegisterReceiver(receiver, new IntentFilter(_permissionAction));

        try
        {
            var intent = new Intent(_permissionAction).SetPackage(_context.PackageName);
            // Android 12 起系统需要向 PendingIntent 回填授权结果，必须可修改
            var flags = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? PendingIntentFlags.Mutable
                : (PendingIntentFlags)0;
            var pendingIntent = PendingIntent.GetBroadcast(_context, 0, intent, flags)
                ?? throw new InvalidOperationException("创建 USB 权限 PendingIntent 失败。");

            _usbManager.RequestPermission(device, pendingIntent);

            using var timeout = new CancellationTokenSource(PermissionTimeoutMs);
            using var registration = timeout.Token.Register(() => completion.TrySetResult(false));
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _context.UnregisterReceiver(receiver);
            }
            catch (ArgumentException)
            {
                // 未注册成功时忽略
            }
        }
    }

    /// <summary>
    /// 按系统版本选择接收器注册方式：Android 13 起必须显式声明是否导出
    /// </summary>
    private void RegisterReceiver(BroadcastReceiver receiver, IntentFilter filter)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            return;
        }

        _context.RegisterReceiver(receiver, filter);
    }

    /// <summary>
    /// 释放平台连接；调用方需持有 <see cref="_gate"/>
    /// </summary>
    private void ReleaseConnection()
    {
        if (_connection is null)
        {
            return;
        }

        if (_interface is not null)
        {
            _connection.ReleaseInterface(_interface);
        }

        _connection.Close();
        _connection = null;
        _interface = null;
    }

    /// <summary>
    /// 接收 USB 授权结果
    /// </summary>
    private sealed class UsbPermissionReceiver(TaskCompletionSource<bool> completion) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var granted = intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false;
            completion.TrySetResult(granted);
        }
    }

    /// <summary>
    /// 接收 USB 拔出广播
    /// </summary>
    private sealed class UsbDetachReceiver(ushort vendorId, ushort productId, Action onDetached) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (GetDevice(intent) is { } device
                && device.VendorId == vendorId
                && device.ProductId == productId)
            {
                onDetached();
            }
        }

        private static UsbDevice? GetDevice(Intent? intent)
        {
            if (intent is null)
            {
                return null;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                return intent.GetParcelableExtra(
                    UsbManager.ExtraDevice,
                    Java.Lang.Class.FromType(typeof(UsbDevice))) as UsbDevice;
            }

#pragma warning disable CS0618 // 旧系统没有类型化重载
            return intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
#pragma warning restore CS0618
        }
    }
}
