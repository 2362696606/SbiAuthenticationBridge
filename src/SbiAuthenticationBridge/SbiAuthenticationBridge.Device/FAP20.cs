using System.Security.Cryptography;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Device.Fcg3M115;
using SbiAuthenticationBridge.Device.Protocol;
using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;
using Serilog;

namespace SbiAuthenticationBridge.Device;

// ReSharper disable once InconsistentNaming
public class FAP20 : IDevice, IDeviceKeyRotation
{
    #region Consts

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "FAP20");

    /// <summary>
    /// 证书 DER 的最大长度，对应 LOAD_CERTIFICATE(0x11) 协议限制
    /// </summary>
    private const int MaxCertificateLength = 3500;

    /// <summary>
    /// FCG-3M115 安全采图请求长度
    /// </summary>
    public const int SecureCaptureRequestLength = 336;

    /// <summary>
    /// FCG-3M115 安全采图响应长度
    /// </summary>
    public const int SecureCaptureResponseLength = 124_104;

    /// <summary>
    /// 原生接口不提供响应长度，使用足够大的缓冲区接收回执或错误信息
    /// </summary>
    private const int ResponseBufferSize = 64 * 1024;

    /// <summary>
    /// 证书下发的默认超时：证书走慢速控制传输，默认 5 秒
    /// </summary>
    public const uint DefaultCertificateTimeoutMs = 5_000;

    /// <summary>
    /// 默认 USB 厂商 ID，与 <c>Lib/ICT/FAP20/FingerPrintDebug.INI</c> 中的首个型号一致
    /// </summary>
    public const ushort DefaultVendorId = 0x0483;

    /// <summary>
    /// 默认 USB 产品 ID，与 <c>Lib/ICT/FAP20/FingerPrintDebug.INI</c> 中的首个型号一致
    /// </summary>
    public const ushort DefaultProductId = 0x5725;

    /// <summary>
    /// 通用命令的默认超时
    /// </summary>
    public const uint DefaultTimeoutMs = 1_000;

    /// <summary>
    /// 采集命令的默认超时：需要等待用户放手指
    /// </summary>
    public const uint DefaultCaptureTimeoutMs = 30_000;

    /// <summary>
    /// 原生接口返回的设备忙/需复位状态码
    /// </summary>
    internal const int DeviceBusyResultCode = 0x75;

    /// <summary>
    /// libusb 的 <c>LIBUSB_ERROR_NO_DEVICE</c>：设备被拔出或重新枚举
    /// </summary>
    internal const int DeviceNotPresentResultCode = -4;

    /// <summary>
    /// 收到 0x75 后自动软复位的超时
    /// </summary>
    private const uint AutoResetTimeoutMs = 2_000;

    /// <summary>
    /// 手动软复位的默认超时
    /// </summary>
    public const uint DefaultDeviceResetTimeoutMs = 5_000;

    #endregion

    #region Fileds

    /// <summary>
    /// 日志是否已初始化
    /// </summary>
    private bool _logInitialized;

    /// <summary>
    /// 设备句柄
    /// </summary>
    private IntPtr _deviceHandle = IntPtr.Zero;

    /// <summary>
    /// 保护设备句柄的打开与关闭，避免并发连接/断开造成句柄泄漏
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// USB 物理在线监测器
    /// </summary>
    private readonly UsbPresenceMonitor _presenceMonitor;

    /// <summary>
    /// 会话是否可用；物理断开时由监测器置为 false
    /// </summary>
    private volatile bool _isConnected;

    /// <summary>
    /// 设备句柄版本号，用于防止物理断开后的延迟关闭误伤新句柄
    /// </summary>
    private int _handleGeneration;

    /// <summary>
    /// 是否已释放
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// USB 厂商 ID
    /// </summary>
    private readonly ushort _vendorId;

    /// <summary>
    /// USB 产品 ID
    /// </summary>
    private readonly ushort _productId;

    /// <summary>
    /// Android 宿主打开的 USB 会话；桌面为 null
    /// </summary>
    private IFap20UsbSession? _usbSession;

    /// <summary>
    /// 当前连接是否已上传 Host 证书
    /// </summary>
    private volatile bool _hostCertificateLoaded;

    /// <summary>
    /// 当前连接是否已加载旧版 SERVER_ENCRYPTION 证书
    /// </summary>
    private volatile bool _encryptionCertificateLoaded;

    #endregion

    /// <summary>
    /// 平台 USB 打开器。Android 宿主设置后使用 <c>ftm_open_fd</c> 打开设备；
    /// 为 null 时使用 <c>ftm_open</c>（桌面/Linux）。
    /// </summary>
    /// <remarks>须在调用 <see cref="ConnectAsync"/> 之前设置。</remarks>
    public static IFap20UsbHost? UsbHost { get; set; }

    /// <summary>
    /// 使用 <c>FingerPrintDebug.INI</c> 中的默认 VID/PID 创建 FAP20 设备实例
    /// </summary>
    public FAP20()
        : this(DefaultVendorId, DefaultProductId)
    {
    }

    /// <summary>
    /// 创建 FAP20 设备实例
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID，须与 FingerPrintDebug.INI 一致</param>
    /// <param name="productId">USB 产品 ID，须与 FingerPrintDebug.INI 一致</param>
    public FAP20(ushort vendorId, ushort productId)
    {
        _vendorId = vendorId;
        _productId = productId;
        _presenceMonitor = new UsbPresenceMonitor(vendorId, productId);
        _presenceMonitor.PresenceChanged += OnPresenceChanged;
        _presenceMonitor.Start();
    }

    #region AsyncDispose

    private void ReleaseUnmanagedResources()
    {
        lock (_syncRoot)
        {
            CloseDevice();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presenceMonitor.PresenceChanged -= OnPresenceChanged;

        try
        {
            await _presenceMonitor.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                CloseDevice();
            }

            GC.SuppressFinalize(this);
        }
    }

    ~FAP20()
    {
        ReleaseUnmanagedResources();
    }

    #endregion

    #region IDevice

    public async Task ConnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 已连接时保持幂等
        if (IsConnected)
        {
            return;
        }

        // Android 无法枚举 /dev/bus/usb：由宿主完成 UsbManager 授权、打开连接、ClaimInterface，
        // 再把文件描述符交给 ftm_open_fd。
        IFap20UsbSession? usbSession = null;
        if (UsbHost is { } usbHost)
        {
            usbSession = await usbHost.OpenAsync(_vendorId, _productId).ConfigureAwait(false)
                         ?? throw new InvalidOperationException(
                             "打开 FAP20 的 USB 设备失败，请确认设备已插入并已授权 USB 访问。");
        }

        await Task.Run(() => ConnectCore(usbSession)).ConfigureAwait(false);
    }

    /// <summary>
    /// 在设备锁内完成连接；未成功接管 <paramref name="usbSession"/> 时负责释放它
    /// </summary>
    private void ConnectCore(IFap20UsbSession? usbSession)
    {
        lock (_syncRoot)
        {
            var attached = false;

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // 并发连接时可能已被另一路径打开
                if (_isConnected && _deviceHandle != IntPtr.Zero)
                {
                    return;
                }

                // 桌面通过 ftm_open() 自行枚举；Android 已由宿主确认设备存在
                if (usbSession is null && !_presenceMonitor.Refresh())
                {
                    throw new InvalidOperationException("FAP20 设备未插入（USB 物理连接不存在），无法连接。");
                }

                // 物理断开后旧句柄可能已经失效，先释放再重新打开
                CloseDevice();

                if (!_logInitialized)
                {
                    // Android 宿主已用 ftm_log_init_path 指定可写路径，不要再用默认路径覆盖
                    if (!Fap20AndroidSdk.IsLogConfigured)
                    {
                        FtmNativeMethods.LogInit((int)FtmLogLevel.Warn);
                    }

                    _logInitialized = true;
                }

                var handle = usbSession?.Handle ?? FtmNativeMethods.Open();
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "打开 FAP20 设备失败，请检查设备连接及 ftmBridgeAPI 依赖是否可加载。");
                }

                _deviceHandle = handle;
                _usbSession = usbSession;
                attached = true;
                _handleGeneration++;

                // 桌面打开过程中也可能被拔出；Android 由 UsbDeviceConnection 报告错误
                if (_disposed || (usbSession is null && !_presenceMonitor.Refresh()))
                {
                    CloseDevice();
                    throw new InvalidOperationException("设备在连接过程中被拔出或已释放。");
                }

                _isConnected = true;
                Logger.Information("FAP20 设备已连接");
            }
            finally
            {
                if (!attached)
                {
                    usbSession?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 断开设备
    /// </summary>
    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            lock (_syncRoot)
            {
                _isConnected = false;
                CloseDevice();
            }

            Logger.Information("FAP20 设备已断开");
        });
    }

    /// <summary>
    /// 会话是否可用：句柄已打开且 USB 物理连接仍在
    /// </summary>
    public bool IsConnected => _isConnected && _deviceHandle != IntPtr.Zero;

    /// <summary>
    /// 设置设备工作状态（ftm_setDevStatus）
    /// </summary>
    /// <param name="status">目标状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default)
    {
        var ftmStatus = status switch
        {
            DeviceStatus.Ready => FtmDeviceStatus.Ready,
            DeviceStatus.Busy => FtmDeviceStatus.Busy,
            DeviceStatus.NotReady => FtmDeviceStatus.NotReady,
            DeviceStatus.NotRegistered => FtmDeviceStatus.NotRegistered,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

        await SetDevStatusAsync(ftmStatus, cancellationToken: cancellationToken).ConfigureAwait(false);
        Logger.Information("FAP20 设备状态已设置为 {Status}", status);
    }

    /// <summary>
    /// 操作系统当前是否识别到该 USB 设备
    /// </summary>
    public bool IsPhysicallyConnected => _presenceMonitor.IsPresent;

    /// <summary>
    /// 关闭设备并复位句柄，调用方需持有 <see cref="_syncRoot"/>
    /// </summary>
    private void CloseDevice()
    {
        _isConnected = false;
        _hostCertificateLoaded = false;
        _encryptionCertificateLoaded = false;

        var handle = _deviceHandle;
        _deviceHandle = IntPtr.Zero;

        // 先让 SDK 释放对 fd 的引用，再关闭宿主的 UsbDeviceConnection（Android）
        var usbSession = _usbSession;
        _usbSession = null;

        if (handle != IntPtr.Zero)
        {
            _handleGeneration++;
            FtmNativeMethods.Close(handle);
        }

        usbSession?.Dispose();
    }

    /// <summary>
    /// USB 物理连接状态改变时触发
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="isPresent"></param>
    private void OnPresenceChanged(object? sender, bool isPresent)
    {
        if (isPresent || _disposed)
        {
            return;
        }

        // 先让属性立即反映断开，再异步释放可能已经失效的句柄
        _isConnected = false;
        var generation = Volatile.Read(ref _handleGeneration);
        _ = Task.Run(() => CloseAfterPhysicalLoss(generation));
    }

    /// <summary>
    /// 物理断开后释放设备句柄
    /// </summary>
    /// <param name="generation"></param>
    private void CloseAfterPhysicalLoss(int generation)
    {
        try
        {
            lock (_syncRoot)
            {
                // 连接流程可能已经换成新句柄，旧事件不得误关新连接
                if (_disposed || generation != _handleGeneration)
                {
                    return;
                }

                CloseDevice();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "物理断开后释放设备句柄失败");
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 向旧固件加载 SERVER_ENCRYPTION 证书（LOAD_CERTIFICATE 0x11）
    /// </summary>
    /// <remarks>
    /// 旧固件用该证书加密 SBI 响应中的会话密钥；证书保存在设备 RAM 中，
    /// 设备复位或重新枚举后需要重新加载。新 SDK 的 <c>ftm_load_certificate</c>
    /// 直接接收 DER，由 SDK 负责旧固件的帧封装，不再回读安装摘要。
    /// </remarks>
    /// <param name="certificateDer">DER 编码的 X.509 证书，长度为 1..3500 字节</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下发证书的 SHA-256（大写十六进制）</returns>
    public Task<string> LoadCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultCertificateTimeoutMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (certificateDer.IsEmpty)
        {
            throw new ArgumentException("证书 DER 不能为空。", nameof(certificateDer));
        }

        if (certificateDer.Length > MaxCertificateLength)
        {
            throw new ArgumentException(
                $"证书 DER 长度不能超过 {MaxCertificateLength} 字节，实际为 {certificateDer.Length} 字节。",
                nameof(certificateDer));
        }

        var der = certificateDer.ToArray();

        return Task.Run(() =>
        {
            ExecuteRequest("ftm_load_certificate", der, timeoutMs,
                FtmNativeMethods.LoadCertificate);

            var fingerprint = Convert.ToHexString(SHA256.HashData(der));
            _encryptionCertificateLoaded = true;
            Logger.Information("FAP20 SERVER_ENCRYPTION 证书已加载，{Length} 字节，SHA-256 {Fingerprint}",
                der.Length, fingerprint);

            return fingerprint;
        }, cancellationToken);
    }

    /// <summary>
    /// 确保当前连接已加载旧版 SERVER_ENCRYPTION 证书
    /// </summary>
    /// <param name="certificateDer">DER 编码的 X.509 证书</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task EnsureEncryptionCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultCertificateTimeoutMs, CancellationToken cancellationToken = default)
    {
        if (_encryptionCertificateLoaded && IsConnected)
        {
            return;
        }

        await LoadCertificateAsync(certificateDer, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 向设备上传 Host 证书（FCG-3M115 安全采图要求，每次连接调用一次）
    /// </summary>
    /// <remarks>
    /// 新 SDK 的 <c>ftm_load_certificate()</c> 直接接收 DER 证书，不再使用旧的 37 字节帧头，
    /// 也没有回读摘要；返回 0 表示设备已接受证书。设备重新上电、复位或重新打开句柄后需重新上传。
    /// </remarks>
    /// <param name="certificateDer">Host 证书 DER，长度为 1..3500 字节</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="ArgumentException">证书为空或超过协议允许的最大长度</exception>
    /// <exception cref="InvalidOperationException">设备未连接或下发失败</exception>
    public Task LoadHostCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultCertificateTimeoutMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (certificateDer.IsEmpty)
        {
            throw new ArgumentException("证书 DER 不能为空。", nameof(certificateDer));
        }

        if (certificateDer.Length > MaxCertificateLength)
        {
            throw new ArgumentException(
                $"证书 DER 长度不能超过 {MaxCertificateLength} 字节，实际为 {certificateDer.Length} 字节。",
                nameof(certificateDer));
        }

        // 持有副本，避免调用方在异步执行期间复用或修改缓冲区
        var der = certificateDer.ToArray();

        return Task.Run(() =>
        {
            ExecuteRequest("ftm_load_certificate", der, timeoutMs,
                FtmNativeMethods.LoadCertificate);

            _hostCertificateLoaded = true;
            Logger.Information("FAP20 Host 证书已上传，{Length} 字节", der.Length);
        }, cancellationToken);
    }

    /// <summary>
    /// 确保当前连接已上传 Host 证书
    /// </summary>
    /// <param name="certificateDer">Host 证书 DER</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task EnsureHostCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultCertificateTimeoutMs, CancellationToken cancellationToken = default)
    {
        if (_hostCertificateLoaded && IsConnected)
        {
            return;
        }

        await LoadHostCertificateAsync(certificateDer, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// FCG-3M115 安全采图：发送 336 字节签名请求，返回 124104 字节二进制响应
    /// </summary>
    /// <remarks>
    /// 调用前必须先通过 <see cref="EnsureHostCertificateAsync"/> 上传 Host 证书。
    /// 返回值是加密响应，需由 <c>Fcg3M115Host</c> 验签、解包并解密后才能得到图像。
    /// </remarks>
    /// <param name="request">336 字节 FCG-3M115 签名请求</param>
    /// <param name="timeoutMs">采图超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>124104 字节 FCG-3M115 响应</returns>
    /// <exception cref="ArgumentException">请求长度不是 336 字节</exception>
    /// <exception cref="InvalidOperationException">未上传 Host 证书、设备未连接或设备返回失败</exception>
    public Task<byte[]> CaptureSecureImageAsync(ReadOnlyMemory<byte> request,
        uint timeoutMs = DefaultCaptureTimeoutMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (request.Length != SecureCaptureRequestLength)
        {
            throw new ArgumentException(
                $"FCG-3M115 请求长度必须为 {SecureCaptureRequestLength} 字节，实际为 {request.Length} 字节。",
                nameof(request));
        }

        if (!_hostCertificateLoaded)
        {
            throw new InvalidOperationException("尚未向设备上传 Host 证书，无法执行安全采图。");
        }

        var payload = request.ToArray();

        return Task.Run(() =>
        {
            try
            {
                var (result, response) = ExecuteRequestRaw("ftm_capture_auth", payload,
                    SecureCaptureResponseLength, timeoutMs, FtmNativeMethods.CaptureAuth);

                Fcg3M115MessageLog.Write("capture-auth", payload, result, response);

                if (result != 0)
                {
                    var detail = DescribeUnexpectedResponse(response);
                    Logger.Warning("ftm_capture_auth 返回错误码 {Result}（0x{ResultHex}）：{Detail}",
                        result, result, detail);

                    throw new FtmCommandException("ftm_capture_auth", result,
                        $"ftm_capture_auth 失败，返回错误码 0x{result:X}（{result}）。{detail}");
                }

                if (!IsFcp1(response))
                {
                    var detail = DescribeUnexpectedResponse(response);
                    Logger.Warning("ftm_capture_auth 非 FCP1 响应：{Detail}", detail);

                    throw new InvalidOperationException(
                        $"ftm_capture_auth 返回的数据不是 FCG-3M115 FCP1 响应。{detail}");
                }

                return response;
            }
            catch
            {
                // 采图失败可能伴随 USB 复位；下次调用需重新上传 Host 证书
                _hostCertificateLoaded = false;
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 发送 336 字节认证采集请求并返回模组原始响应，不做 FCP1 校验
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用于确认固件实际实现的采集协议：旧固件返回 JSON，FCG-3M115 固件返回
    /// 124104 字节、以 <c>FCP1</c> 开头的响应。
    /// </para>
    /// <para>
    /// 调用前应先通过 <see cref="EnsureHostCertificateAsync"/> 下发 Host 证书。
    /// 本方法不会因原生返回码非零或响应格式不符而抛出设备错误，便于记录原始返回。
    /// </para>
    /// </remarks>
    /// <param name="request">336 字节请求；签名无效时模组会拒绝采集，但仍会返回协议响应</param>
    /// <param name="timeoutMs">采图超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>原生返回码、响应缓冲区与协议判定</returns>
    public Task<Fap20CaptureProbe> ProbeCaptureAuthAsync(ReadOnlyMemory<byte> request,
        uint timeoutMs = DefaultCaptureTimeoutMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (request.Length != SecureCaptureRequestLength)
        {
            throw new ArgumentException(
                $"FCG-3M115 请求长度必须为 {SecureCaptureRequestLength} 字节，实际为 {request.Length} 字节。",
                nameof(request));
        }

        var payload = request.ToArray();

        return Task.Run(() =>
        {
            var (result, response) = ExecuteRequestRaw("ftm_capture_auth", payload,
                SecureCaptureResponseLength, timeoutMs, FtmNativeMethods.CaptureAuth);

            Fcg3M115MessageLog.Write("capture-probe", payload, result, response);

            var isFcp1 = result == 0 && IsFcp1(response);
            var text = isFcp1 ? null : FtmProtocolCodec.ReadResponseText(response);

            var probe = new Fap20CaptureProbe(result, response, isFcp1, text);

            Logger.Information(
                "协议探测：原生返回码 {Result}（0x{ResultHex}），{Description}",
                result, result, probe.Describe());

            return probe;
        }, cancellationToken);
    }

    /// <summary>
    /// 探测设备在线状态与基本能力（DISCOVER 0x01）
    /// </summary>
    /// <param name="callbackId">设备服务回调地址，1..192 字节可打印 ASCII</param>
    /// <param name="deviceType">设备类型</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备发现响应</returns>
    public Task<DiscoveryResponse> DiscoverAsync(string callbackId,
        FtmDeviceType deviceType = FtmDeviceType.Finger,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(callbackId);

        var request = FtmProtocolCodec.EncodeDiscover(callbackId, DateTimeOffset.UtcNow, deviceType);

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_discover", request, timeoutMs,
                FtmNativeMethods.Discover);

            return FtmProtocolCodec.ParseDiscoverResponse(response);
        }, cancellationToken);
    }

    /// <summary>
    /// 获取设备详细信息（DEVICE_INFO 0x02）
    /// </summary>
    /// <param name="callbackId">设备服务回调地址，1..192 字节可打印 ASCII</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备信息响应</returns>
    public Task<DeviceInfoResponse> GetDeviceInfoAsync(string callbackId,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(callbackId);

        var request = FtmProtocolCodec.EncodeDeviceInfo(callbackId, DateTimeOffset.UtcNow);

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_get_device_info", request, timeoutMs,
                FtmNativeMethods.GetDeviceInfo);

            return FtmProtocolCodec.ParseDeviceInfoResponse(response);
        }, cancellationToken);
    }

    /// <summary>
    /// 同步时间到设备（SET_TIME 0x03）
    /// </summary>
    /// <remarks>设备只校验并回显时间，不保存 RTC，也不影响其他命令。</remarks>
    /// <param name="utc">要下发的时刻，带偏移的时间会自动折算为 UTC</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetTimeAsync(DateTimeOffset utc,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = FtmProtocolCodec.EncodeSetTime(utc);

        return Task.Run(
            () => ExecuteRequestOnly("ftm_setTime", request, timeoutMs, FtmNativeMethods.SetTime),
            cancellationToken);
    }

    /// <summary>
    /// 认证比对场景的指纹采集（CAPTURE_AUTH 0x20）
    /// </summary>
    /// <remarks>
    /// 旧固件使用本方法返回 SBI JSON；FCG-3M115 固件请使用
    /// <see cref="CaptureSecureImageAsync"/>。返回设备原始 JSON 文本，调用方需自行检查其中的
    /// <c>error.errorCode</c>；101 表示还没有检测到手指，并不表示原生命令失败。
    /// </remarks>
    /// <param name="request">采集参数</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备采集响应 JSON</returns>
    public Task<string> CaptureAuthAsync(FtmCaptureRequest request,
        uint timeoutMs = DefaultCaptureTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var payload = FtmProtocolCodec.EncodeCapture(request, registration: false);

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_capture_auth", payload, timeoutMs,
                FtmNativeMethods.CaptureAuth);

            return FtmProtocolCodec.ReadResponseText(response);
        }, cancellationToken);
    }

    /// <summary>
    /// 用户注册场景的指纹采集（CAPTURE_REG 0x21）
    /// </summary>
    /// <param name="request">采集参数</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备采集响应 JSON</returns>
    [Obsolete("旧版 JSON 采集协议；新固件请使用 CaptureSecureImageAsync。")]
    public Task<string> CaptureRegAsync(FtmCaptureRequest request,
        uint timeoutMs = DefaultCaptureTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var payload = FtmProtocolCodec.EncodeCapture(request, registration: true);

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_capture_reg", payload, timeoutMs,
                FtmNativeMethods.CaptureReg);

            return FtmProtocolCodec.ReadResponseText(response);
        }, cancellationToken);
    }

    /// <summary>
    /// 读取设备 DKL 公钥（GET_DKL1_PUBLIC_KEY 0x30）
    /// </summary>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DKL 公钥与结果码</returns>
    public Task<FtmDklPublicKeyResult> GetDklPublicKeyAsync(
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = FtmProtocolCodec.EncodeGetDklPublicKey();

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_get_dkl_public_key", request, timeoutMs,
                FtmNativeMethods.GetDklPublicKey);

            return FtmProtocolCodec.ParseDklPublicKeyResult(response);
        }, cancellationToken);
    }

    /// <summary>
    /// 下发 DKL 证书（SET_DKL1_CERTIFICATE 0x31）
    /// </summary>
    /// <param name="certificateDer">DKL 证书 DER，1..3500 字节</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备结果码；除 Ok 外均表示本次操作未完成</returns>
    public Task<FtmDklResult> SetDklCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = FtmProtocolCodec.EncodeSetDklCertificate(certificateDer.Span);

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_set_dkl_certificate", request, timeoutMs,
                FtmNativeMethods.SetDklCertificate);

            return FtmProtocolCodec.ParseDklResult(response);
        }, cancellationToken);
    }

    #region IDeviceKeyRotation

    /// <inheritdoc />
    public bool RequiresCertificate => true;

    /// <summary>
    /// 读取当前设备密钥（GET_DKL1_PUBLIC_KEY 0x30）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前 DKL 公钥与密钥代数</returns>
    /// <exception cref="InvalidOperationException">设备未连接或设备返回失败</exception>
    public async Task<DeviceKeyInfo> GetKeyInfoAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnectedForKeyRotation();

        var result = await GetDklPublicKeyAsync(DefaultTimeoutMs, cancellationToken)
            .ConfigureAwait(false);

        if (result.Result != FtmDklResult.Ok || result.Key is not { } key)
        {
            throw new InvalidOperationException(DescribeDklFailure("读取 DKL 公钥", result.Result));
        }

        return CreateDeviceKeyInfo(key);
    }

    /// <summary>
    /// 更换设备密钥：下发新的 DKL 证书（SET_DKL1_CERTIFICATE 0x31）
    /// </summary>
    /// <remarks>
    /// 证书由设备提供方签发；设备安装证书后按新的密钥代数生成签名密钥，
    /// 随后的 SBI 响应应携带新的证书与签名，供 CTK 的 Key Rotation 用例核对。
    /// </remarks>
    /// <param name="certificateDer">新的 DKL 证书（DER）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>轮换后的 DKL 公钥与密钥代数</returns>
    /// <exception cref="ArgumentException"><paramref name="certificateDer"/> 为空</exception>
    /// <exception cref="InvalidOperationException">设备未连接或设备返回失败</exception>
    public async Task<DeviceKeyInfo> RotateKeyAsync(ReadOnlyMemory<byte>? certificateDer = null,
        CancellationToken cancellationToken = default)
    {
        if (certificateDer is not { IsEmpty: false })
        {
            throw new ArgumentException("FAP20 更换密钥需要提供 DKL 证书（DER）。",
                nameof(certificateDer));
        }

        EnsureConnectedForKeyRotation();

        // 设备安装证书并生成新密钥可能较慢，使用证书下发的 5 秒超时
        var result = await SetDklCertificateAsync(certificateDer.Value, DefaultCertificateTimeoutMs,
            cancellationToken).ConfigureAwait(false);

        if (result != FtmDklResult.Ok)
        {
            throw new InvalidOperationException(DescribeDklFailure("更换 DKL 证书", result));
        }

        var info = await GetKeyInfoAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information("FAP20 设备密钥已更换，代数 {RotationId}，指纹 {Fingerprint:l}",
            info.RotationId, info.Fingerprint);

        return info;
    }

    /// <summary>
    /// 密钥轮换前确认设备已连接
    /// </summary>
    private void EnsureConnectedForKeyRotation()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("FAP20 设备未连接，无法更换密钥。");
        }
    }

    /// <summary>
    /// 构造界面使用的设备密钥信息
    /// </summary>
    private static DeviceKeyInfo CreateDeviceKeyInfo(FtmDklPublicKey key) => new()
    {
        RotationId = key.RotationId,
        SubjectPublicKeyInfo = key.SubjectPublicKeyInfo,
        Fingerprint = Convert.ToHexString(SHA256.HashData(key.SubjectPublicKeyInfo.Span))
    };

    /// <summary>
    /// 把 DKL 结果码转换为可读的失败原因
    /// </summary>
    private static string DescribeDklFailure(string operation, FtmDklResult result) => result switch
    {
        FtmDklResult.InvalidData => $"{operation}失败：证书或密钥代数不合法。",
        FtmDklResult.Busy => $"{operation}失败：设备忙，请稍后重试。",
        FtmDklResult.RotateFailed => $"{operation}失败：设备密钥生成或写入失败。",
        _ => $"{operation}失败：{result}。"
    };

    #endregion

    /// <summary>
    /// 经 USB Bulk OUT 端点发送原始数据（ftm_send）
    /// </summary>
    /// <param name="data">原始字节</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SendAsync(ReadOnlyMemory<byte> data,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty)
        {
            throw new ArgumentException("发送数据不能为空。", nameof(data));
        }

        var payload = data.ToArray();

        return Task.Run(
            () => ExecuteRequestOnly("ftm_send", payload, timeoutMs, FtmNativeMethods.Send),
            cancellationToken);
    }

    /// <summary>
    /// 从 USB Bulk IN 端点接收原始数据（ftm_recv）
    /// </summary>
    /// <remarks>原生接口不返回实际长度，结果按首个 NUL 截断。</remarks>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>接收到的数据</returns>
    public Task<byte[]> RecvAsync(uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() =>
        {
            var response = ExecuteReceive("ftm_recv", timeoutMs);
            return FtmProtocolCodec.TrimAtNul(response).ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// 设置设备工作状态（ftm_setDevStatus）
    /// </summary>
    /// <param name="status">目标状态</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetDevStatusAsync(FtmDeviceStatus status,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() =>
        {
            lock (_syncRoot)
            {
                EnsureConnected("ftm_setDevStatus");

                var result = FtmNativeMethods.SetDevStatus(_deviceHandle, (byte)status, timeoutMs);
                ThrowIfFailed(result, "ftm_setDevStatus");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 触发设备软复位（ftm_device_reset）
    /// </summary>
    /// <remarks>
    /// 复位会清空设备内部采集状态、图像缓冲和工作状态，USB 会话与句柄保持不变。
    /// 收到 0x75 时的自动复位只执行一次，当前命令仍按失败处理，是否重试由调用方决定。
    /// </remarks>
    /// <param name="timeoutMs">复位超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="InvalidOperationException">设备未连接或复位失败</exception>
    public Task DeviceResetAsync(uint timeoutMs = DefaultDeviceResetTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() =>
        {
            lock (_syncRoot)
            {
                EnsureConnected("ftm_device_reset");

                var result = FtmNativeMethods.DeviceReset(_deviceHandle, timeoutMs);
                if (result != 0)
                {
                    throw new FtmCommandException("ftm_device_reset", result,
                        $"ftm_device_reset 失败，返回错误码 0x{result:X}（{result}）。");
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 确认设备处于可执行命令的状态；调用方需持有 <see cref="_syncRoot"/>
    /// </summary>
    private void EnsureConnected(string operation)
    {
        if (!_isConnected || _deviceHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"FAP20 设备未连接，无法执行 {operation}。");
        }
    }

    /// <summary>
    /// 执行带请求和响应缓冲区的原生命令
    /// </summary>
    private byte[] ExecuteRequest(string operation, byte[] request, uint timeoutMs,
        RequestCommand command) =>
        ExecuteRequest(operation, request, ResponseBufferSize, timeoutMs, command);

    /// <summary>
    /// 执行带指定响应缓冲区长度的原生命令（如 124104 字节的安全采图响应）
    /// </summary>
    private byte[] ExecuteRequest(string operation, byte[] request, int responseLength,
        uint timeoutMs, RequestCommand command)
    {
        var response = new byte[responseLength];

        lock (_syncRoot)
        {
            EnsureConnected(operation);

            int result;

            unsafe
            {
                fixed (byte* requestPointer = request)
                fixed (byte* responsePointer = response)
                {
                    result = command(_deviceHandle, (IntPtr)requestPointer, request.Length,
                        (IntPtr)responsePointer, timeoutMs);
                }
            }

            ThrowIfFailed(result, operation);
        }

        return response;
    }

    /// <summary>
    /// 执行原生命令并同时返回原生返回码与响应缓冲区，不把非零返回码转换成异常
    /// </summary>
    /// <remarks>
    /// 旧固件在 <c>ftm_capture_auth</c> 中可能返回 JSON 文本；新固件返回 FCP1 二进制。
    /// 只有保留原始缓冲区才能判断固件实际实现的协议。
    /// </remarks>
    private (int Result, byte[] Response) ExecuteRequestRaw(string operation, byte[] request,
        int responseLength, uint timeoutMs, RequestCommand command)
    {
        var response = new byte[responseLength];

        lock (_syncRoot)
        {
            EnsureConnected(operation);

            int result;
            unsafe
            {
                fixed (byte* requestPointer = request)
                fixed (byte* responsePointer = response)
                {
                    result = command(_deviceHandle, (IntPtr)requestPointer, request.Length,
                        (IntPtr)responsePointer, timeoutMs);
                }
            }

            return (result, response);
        }
    }

    /// <summary>
    /// 响应是否以 FCG-3M115 的 <c>FCP1</c> 魔数开头
    /// </summary>
    private static bool IsFcp1(ReadOnlySpan<byte> response) =>
        response.Length >= 4
        && response[0] == (byte)'F'
        && response[1] == (byte)'C'
        && response[2] == (byte)'P'
        && response[3] == (byte)'1';

    /// <summary>
    /// 描述非预期响应：尽量输出文本（旧固件 JSON），否则输出前 64 字节十六进制
    /// </summary>
    private static string DescribeUnexpectedResponse(ReadOnlySpan<byte> response)
    {
        var text = FtmProtocolCodec.ReadResponseText(response);
        if (!string.IsNullOrWhiteSpace(text) && IsPrintableText(text))
        {
            const int limit = 512;
            return text.Length <= limit
                ? $"设备响应：{text}"
                : $"设备响应（截断）：{text[..limit]}…";
        }

        var length = Math.Min(response.Length, 64);
        return $"设备响应前 {length} 字节：{Convert.ToHexString(response[..length])}";
    }

    private static bool IsPrintableText(string text) =>
        text.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t');

    /// <summary>
    /// 执行只有请求、没有响应缓冲区的原生命令
    /// </summary>
    private void ExecuteRequestOnly(string operation, byte[] request, uint timeoutMs,
        RequestOnlyCommand command)
    {
        lock (_syncRoot)
        {
            EnsureConnected(operation);

            int result;

            unsafe
            {
                fixed (byte* requestPointer = request)
                {
                    result = command(_deviceHandle, (IntPtr)requestPointer, request.Length, timeoutMs);
                }
            }

            ThrowIfFailed(result, operation);
        }
    }

    /// <summary>
    /// 执行只有响应缓冲区的原生命令
    /// </summary>
    private byte[] ExecuteReceive(string operation, uint timeoutMs)
    {
        var response = new byte[ResponseBufferSize];

        lock (_syncRoot)
        {
            EnsureConnected(operation);

            int result;

            unsafe
            {
                fixed (byte* responsePointer = response)
                {
                    result = FtmNativeMethods.Recv(_deviceHandle, (IntPtr)responsePointer,
                        response.Length, timeoutMs);
                }
            }

            ThrowIfFailed(result, operation);
        }

        return response;
    }

    private delegate int RequestCommand(IntPtr handle, IntPtr request, int requestLength,
        IntPtr response, uint timeoutMs);

    private delegate int RequestOnlyCommand(IntPtr handle, IntPtr request, int requestLength,
        uint timeoutMs);

    /// <summary>
    /// 统一处理原生命令返回码：0x75 时尝试一次软复位，当前命令仍按失败处理
    /// </summary>
    private void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        if (result == DeviceNotPresentResultCode)
        {
            // 设备已重新枚举，当前 fd/句柄已失效；关闭后由下一次操作重新连接
            Logger.Warning("{Operation} 失败：USB 设备已断开或重新枚举，关闭陈旧句柄", operation);
            CloseDevice();
        }

        var resetAttempted = result == DeviceBusyResultCode && ResetAfterDeviceBusy(operation);
        var resetMessage = resetAttempted
            ? "已尝试软复位，可重试。"
            : result == DeviceNotPresentResultCode
                ? "设备已重新枚举，请重试。"
                : string.Empty;

        throw new FtmCommandException(operation, result,
            $"{operation} 失败，返回错误码 0x{result:X}（{result}）。{resetMessage}");
    }

    /// <summary>
    /// 0x75 后的尽力恢复；复位失败不覆盖原始错误
    /// </summary>
    private bool ResetAfterDeviceBusy(string operation)
    {
        var handle = _deviceHandle;
        if (handle == IntPtr.Zero)
        {
            Logger.Warning("{Operation:l} 返回 0x75，但设备句柄为空，跳过软复位", operation);
            return false;
        }

        try
        {
            var resetResult = FtmNativeMethods.DeviceReset(handle, AutoResetTimeoutMs);

            if (resetResult == 0)
            {
                Logger.Information("{Operation:l} 返回 0x75，已执行软复位", operation);
            }
            else
            {
                Logger.Warning("{Operation:l} 返回 0x75，软复位失败，reset rc={ResetResult}",
                    operation, resetResult);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "{Operation:l} 返回 0x75，软复位异常", operation);
        }

        return true;
    }

    #endregion
}
