using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Device.Protocol;
using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

namespace SbiAuthenticationBridge.Device;

// ReSharper disable once InconsistentNaming
public class FAP20 : IDevice
{
    #region Consts

    /// <summary>
    /// 证书 DER 的最大长度，对应 LOAD_CERTIFICATE(0x11) 协议限制
    /// </summary>
    private const int MaxCertificateLength = 3500;

    /// <summary>
    /// 原生接口不提供响应长度，使用足够大的缓冲区接收回执或错误信息
    /// </summary>
    /// <remarks>与参考 SDK 的 <c>MAX_RESERVE_DATA_LEN</c>（204800）保持一致。</remarks>
    private const int ResponseBufferSize = 204_800;

    /// <summary>
    /// 证书下发的默认超时：证书走慢速控制传输，默认 5 秒
    /// </summary>
    public const uint DefaultCertificateTimeoutMs = 5_000;

    /// <summary>
    /// 通用命令的默认超时：与参考实现对齐，设备忙或刚复位时 1 秒易超时
    /// </summary>
    public const uint DefaultTimeoutMs = 5_000;

    /// <summary>
    /// 采集命令的默认超时：需要等待用户放手指
    /// </summary>
    public const uint DefaultCaptureTimeoutMs = 30_000;

    /// <summary>
    /// 原生接口返回的设备忙/需复位状态码
    /// </summary>
    internal const int DeviceBusyResultCode = 0x75;

    /// <summary>
    /// libusb 传输超时返回值：设备通常仍在线，可由调用方重试
    /// </summary>
    public const int UsbTimeoutResultCode = -7;

    /// <summary>
    /// 二进制响应日志最多打印的字节数
    /// </summary>
    private const int RawBinaryLogBytes = 512;

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
    /// 清零用的响应缓冲区模板
    /// </summary>
    private static readonly byte[] ZeroResponseBuffer = new byte[ResponseBufferSize];

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

    private readonly ILogger<FAP20>? _logger;

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

    #endregion

    /// <summary>
    /// 创建 FAP20 设备实例
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID，须与 FingerPrintDebug.INI 一致</param>
    /// <param name="productId">USB 产品 ID，须与 FingerPrintDebug.INI 一致</param>
    /// <param name="logger">日志记录器</param>
    public FAP20(ushort vendorId, ushort productId, ILogger<FAP20>? logger = null) : this(
        new UsbPresenceMonitor(vendorId, productId), logger)
    {
    }

    public FAP20(UsbPresenceMonitor usbPresenceMonitor, ILogger<FAP20>? logger = null)
    {
        _presenceMonitor = usbPresenceMonitor;
        _logger = logger;
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

        await Task.Run(() =>
        {
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // 已连接时保持幂等，避免覆盖现有句柄导致泄漏
                if (_isConnected && _deviceHandle != IntPtr.Zero)
                {
                    return;
                }

                if (!_presenceMonitor.Refresh())
                {
                    throw new InvalidOperationException("FAP20 设备未插入（USB 物理连接不存在），无法连接。");
                }

                // 物理断开后旧句柄可能已经失效，先释放再重新打开
                CloseDevice();

                if (!_logInitialized)
                {
                    FtmNativeMethods.LogInit((int)FtmLogLevel.Warn);
                    _logInitialized = true;
                }

                var handle = FtmNativeMethods.Open();
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("打开 FAP20 设备失败，请检查设备连接及 ftmBridgeAPI 依赖是否可加载。");
                }

                _deviceHandle = handle;
                _handleGeneration++;

                // 打开过程中也可能被拔出
                if (_disposed || !_presenceMonitor.Refresh())
                {
                    CloseDevice();
                    throw new InvalidOperationException("设备在连接过程中被拔出或已释放。");
                }

                _isConnected = true;
            }
        }).ConfigureAwait(false);
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
        });
    }

    /// <summary>
    /// 会话是否可用：句柄已打开且 USB 物理连接仍在
    /// </summary>
    public bool IsConnected => _isConnected && _deviceHandle != IntPtr.Zero;

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

        var handle = _deviceHandle;
        _deviceHandle = IntPtr.Zero;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        _handleGeneration++;
        FtmNativeMethods.Close(handle);
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
            _logger?.LogWarning(exception, "物理断开后释放设备句柄失败");
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 向设备加载域加密证书（LOAD_CERTIFICATE）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 请求为 37 字节头（版本/用途/格式/长度/SHA-256）+ DER，由
    /// <see cref="FtmProtocolCodec.EncodeLoadCertificate"/> 生成。
    /// </para>
    /// <para>
    /// 参考 SDK 的 <c>execCmd_ICT</c> 对 LOAD_CERTIFICATE 只下发数据并逐包读取 ACK，
    /// 不返回额外的安装摘要；因此这里以原生返回 0 作为加载成功，摘要由本地计算。
    /// 证书保存在设备 RAM 中，设备断开或复位后需要重新加载。
    /// </para>
    /// </remarks>
    /// <param name="certificateDer">DER 编码的 X.509 证书，长度为 1..3500 字节</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本次下发证书的 SHA-256（大写十六进制）</returns>
    /// <exception cref="ArgumentException">证书为空或超过协议允许的最大长度</exception>
    /// <exception cref="InvalidOperationException">设备未连接或下发失败</exception>
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

        // 持有副本，避免调用方在异步执行期间复用或修改缓冲区
        var der = certificateDer.ToArray();
        var request = FtmProtocolCodec.EncodeLoadCertificate(der);
        LogRequest("ftm_load_certificate", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_load_certificate");

                    var result = FtmNativeMethods.LoadCertificate(_deviceHandle, requestPointer,
                        request.Length, responsePointer, timeoutMs);
                    LogBinaryResponse("ftm_load_certificate", result, responsePointer, 34);
                    ThrowIfFailed(result, "ftm_load_certificate");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(der));
            _logger?.LogDebug(
                "证书已加载，{Length} 字节，SHA-256 {Fingerprint}",
                der.Length,
                fingerprint);

            return fingerprint;
        }, cancellationToken);
    }

    /// <summary>
    /// 探测设备在线状态与基本能力（DISCOVER 0x01）
    /// </summary>
    /// <param name="callbackId">设备服务回调地址，1..192 字节可打印 ASCII</param>
    /// <param name="deviceType">设备类型</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备发现响应 JSON 文本</returns>
    public Task<string> DiscoverAsync(string callbackId,
        FtmDeviceType deviceType = FtmDeviceType.Finger,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(callbackId);

        var request = FtmProtocolCodec.EncodeDiscover(callbackId, DateTimeOffset.UtcNow, deviceType);
        LogRequest("ftm_discover", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_discover");

                    var result = FtmNativeMethods.Discover(_deviceHandle, requestPointer,
                        request.Length, responsePointer, timeoutMs);
                    LogTextResponse("ftm_discover", result, Marshal.PtrToStringUTF8(responsePointer));
                    ThrowIfFailed(result, "ftm_discover");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.ReadResponseText(response);
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 获取设备详细信息（DEVICE_INFO 0x02）
    /// </summary>
    /// <param name="callbackId">设备服务回调地址，1..192 字节可打印 ASCII</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备信息响应 JSON 文本</returns>
    public Task<string> GetDeviceInfoAsync(string callbackId,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(callbackId);

        var request = FtmProtocolCodec.EncodeDeviceInfo(callbackId, DateTimeOffset.UtcNow);
        LogRequest("ftm_get_device_info", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_get_device_info");

                    var result = FtmNativeMethods.GetDeviceInfo(_deviceHandle, requestPointer,
                        request.Length, responsePointer, timeoutMs);
                    LogTextResponse("ftm_get_device_info", result, Marshal.PtrToStringUTF8(responsePointer));
                    ThrowIfFailed(result, "ftm_get_device_info");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.ReadResponseText(response);
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
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
        LogRequest("ftm_setTime", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_setTime");

                    var result = FtmNativeMethods.SetTime(_deviceHandle, requestPointer,
                        request.Length, timeoutMs);
                    LogResult("ftm_setTime", result);
                    ThrowIfFailed(result, "ftm_setTime");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 认证比对场景的指纹采集（CAPTURE_AUTH 0x20）
    /// </summary>
    /// <remarks>
    /// 返回设备原始 JSON 文本，调用方需自行检查其中的 <c>error.errorCode</c>；
    /// 101 表示还没有检测到手指，并不表示原生命令失败。
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
        LogRequest("ftm_capture_auth", payload);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(payload.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(payload, 0, requestPointer, payload.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_capture_auth");

                    var result = FtmNativeMethods.CaptureAuth(_deviceHandle, requestPointer,
                        payload.Length, responsePointer, timeoutMs);
                    LogTextResponse("ftm_capture_auth", result, Marshal.PtrToStringUTF8(responsePointer));
                    ThrowIfFailed(result, "ftm_capture_auth");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.ReadResponseText(response);
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 用户注册场景的指纹采集（CAPTURE_REG 0x21）
    /// </summary>
    /// <param name="request">采集参数</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备采集响应 JSON</returns>
    public Task<string> CaptureRegAsync(FtmCaptureRequest request,
        uint timeoutMs = DefaultCaptureTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var payload = FtmProtocolCodec.EncodeCapture(request, registration: true);
        LogRequest("ftm_capture_reg", payload);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(payload.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(payload, 0, requestPointer, payload.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_capture_reg");

                    var result = FtmNativeMethods.CaptureReg(_deviceHandle, requestPointer,
                        payload.Length, responsePointer, timeoutMs);
                    LogTextResponse("ftm_capture_reg", result, Marshal.PtrToStringUTF8(responsePointer));
                    ThrowIfFailed(result, "ftm_capture_reg");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.ReadResponseText(response);
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
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
        LogRequest("ftm_get_dkl_public_key", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_get_dkl_public_key");

                    var result = FtmNativeMethods.GetDklPublicKey(_deviceHandle, requestPointer,
                        request.Length, responsePointer, timeoutMs);
                    LogBinaryResponse("ftm_get_dkl_public_key", result, responsePointer, RawBinaryLogBytes);
                    ThrowIfFailed(result, "ftm_get_dkl_public_key");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.ParseDklPublicKeyResult(response);
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 下发 DKL 证书（SET_DKL_CERTIFICATE）
    /// </summary>
    /// <remarks>
    /// 参考 SDK 的 <c>execCmd_ICT</c> 对 SET_DKL_CERTIFICATE 与 LOAD_CERTIFICATE 一样，
    /// 只下发数据并逐包读取 ACK，不返回额外结果帧；原生返回 0 即表示设备已接受。
    /// </remarks>
    /// <param name="certificateDer">DKL 证书 DER，1..3500 字节</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备接受时返回 <see cref="FtmDklResult.Ok"/></returns>
    public Task<FtmDklResult> SetDklCertificateAsync(ReadOnlyMemory<byte> certificateDer,
        uint timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = FtmProtocolCodec.EncodeSetDklCertificate(certificateDer.Span);
        LogRequest("ftm_set_dkl_certificate", request);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(request.Length);
            var responsePointer = AllocateResponseBuffer();

            try
            {
                Marshal.Copy(request, 0, requestPointer, request.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_set_dkl_certificate");

                    var result = FtmNativeMethods.SetDklCertificate(_deviceHandle, requestPointer,
                        request.Length, responsePointer, timeoutMs);
                    LogBinaryResponse("ftm_set_dkl_certificate", result, responsePointer, 2);
                    ThrowIfFailed(result, "ftm_set_dkl_certificate");
                }

                return FtmDklResult.Ok;
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
                Marshal.FreeHGlobal(responsePointer);
            }
        }, cancellationToken);
    }

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
        LogRequest("ftm_send", payload);

        return Task.Run(() =>
        {
            var requestPointer = Marshal.AllocHGlobal(payload.Length);

            try
            {
                Marshal.Copy(payload, 0, requestPointer, payload.Length);

                lock (_syncRoot)
                {
                    EnsureConnected("ftm_send");

                    var result = FtmNativeMethods.Send(_deviceHandle, requestPointer,
                        payload.Length, timeoutMs);
                    LogResult("ftm_send", result);
                    ThrowIfFailed(result, "ftm_send");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(requestPointer);
            }
        }, cancellationToken);
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
            var responsePointer = AllocateResponseBuffer();

            try
            {
                lock (_syncRoot)
                {
                    EnsureConnected("ftm_recv");

                    var result = FtmNativeMethods.Recv(_deviceHandle, responsePointer,
                        ResponseBufferSize, timeoutMs);
                    LogBinaryResponse("ftm_recv", result, responsePointer, RawBinaryLogBytes);
                    ThrowIfFailed(result, "ftm_recv");
                }

                var response = new byte[ResponseBufferSize];
                Marshal.Copy(responsePointer, response, 0, response.Length);
                return FtmProtocolCodec.TrimAtNul(response).ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(responsePointer);
            }
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

                LogRequest("ftm_setDevStatus", [(byte)status]);
                var result = FtmNativeMethods.SetDevStatus(_deviceHandle, (byte)status, timeoutMs);
                LogResult("ftm_setDevStatus", result);
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
                LogResult("ftm_device_reset", result);
                if (result != 0)
                {
                    throw new InvalidOperationException(
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
    /// 分配并清零响应缓冲区：SDK 不保证 NUL 结尾，残留旧数据会拼进 JSON
    /// </summary>
    /// <returns>已清零的非托管缓冲区</returns>
    private static IntPtr AllocateResponseBuffer()
    {
        var pointer = Marshal.AllocHGlobal(ResponseBufferSize);
        Marshal.Copy(ZeroResponseBuffer, 0, pointer, ResponseBufferSize);
        return pointer;
    }

    #region 原始收发日志

    /// <summary>
    /// 记录原生命令请求 payload
    /// </summary>
    /// <param name="operation">原生命令名</param>
    /// <param name="payload">紧凑二进制 payload</param>
    private void LogRequest(string operation, ReadOnlySpan<byte> payload)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _logger.LogDebug(
            "{Operation} 请求 {Length}B HEX={Payload}",
            operation,
            payload.Length,
            Convert.ToHexString(payload));
    }

    /// <summary>
    /// 记录文本响应
    /// </summary>
    /// <param name="operation">原生命令名</param>
    /// <param name="resultCode">原生返回码</param>
    /// <param name="response">响应文本</param>
    private void LogTextResponse(string operation, int resultCode, string? response)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _logger.LogDebug(
            "{Operation} 响应 rc=0x{ResultCode:X} TEXT={Response}",
            operation,
            resultCode,
            response ?? string.Empty);
    }

    /// <summary>
    /// 记录二进制响应
    /// </summary>
    /// <param name="operation">原生命令名</param>
    /// <param name="resultCode">原生返回码</param>
    /// <param name="responsePointer">响应缓冲区</param>
    /// <param name="length">响应有效长度</param>
    private void LogBinaryResponse(string operation, int resultCode, IntPtr responsePointer, int length)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var size = Math.Clamp(length, 0, RawBinaryLogBytes);
        var buffer = new byte[size];

        if (size > 0)
        {
            Marshal.Copy(responsePointer, buffer, 0, size);
        }

        _logger.LogDebug(
            "{Operation} 响应 rc=0x{ResultCode:X} {Length}B HEX={Payload}",
            operation,
            resultCode,
            length,
            Convert.ToHexString(buffer));

        if (resultCode != 0)
        {
            _logger.LogDebug(
                "{Operation} 错误响应 TEXT={Response}",
                operation,
                Marshal.PtrToStringUTF8(responsePointer) ?? string.Empty);
        }
    }

    /// <summary>
    /// 记录只有返回码的命令结果
    /// </summary>
    /// <param name="operation">原生命令名</param>
    /// <param name="resultCode">原生返回码</param>
    private void LogResult(string operation, int resultCode)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _logger.LogDebug("{Operation} 返回码 rc=0x{ResultCode:X}", operation, resultCode);
    }

    #endregion

    /// <summary>
    /// 统一处理原生命令返回码：0x75 时尝试一次软复位，当前命令仍按失败处理；
    /// -7 为传输超时，保留句柄供上层重试；其余负数表示会话不可信，释放句柄
    /// </summary>
    /// <remarks>调用方需持有 <see cref="_syncRoot"/>。</remarks>
    private void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        // libusb 超时通常只是本次等待未完成，设备仍在线；关闭句柄会让后续采集全部失败
        if (result == UsbTimeoutResultCode)
        {
            throw new FtmCommandException(operation, result,
                $"{operation} 超时（{result}），设备仍在线，可重试。");
        }

        // 其余负数是 libusb/端口层的错误，设备可能已经重新枚举或传输通道失效，
        // 句柄不能再复用；关闭后 IsConnected 会返回 false，界面可如实显示为断开
        if (result < 0)
        {
            CloseDevice();
            throw new FtmCommandException(operation, result,
                $"{operation} 失败，返回错误码 0x{result:X}（{result}），USB 传输异常，设备已断开。");
        }

        var resetAttempted = result == DeviceBusyResultCode && ResetAfterDeviceBusy(operation);
        var resetMessage = resetAttempted ? "已尝试软复位，可重试。" : string.Empty;

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
            _logger?.LogDebug("{Operation} 返回 0x75，但设备句柄为空，跳过软复位", operation);
            return false;
        }

        try
        {
            var resetResult = FtmNativeMethods.DeviceReset(handle, AutoResetTimeoutMs);

            if (resetResult == 0)
            {
                _logger?.LogDebug("{Operation} 返回 0x75，已执行软复位", operation);
            }
            else
            {
                _logger?.LogDebug(
                    "{Operation} 返回 0x75，软复位失败，reset rc={ResetResult}",
                    operation,
                    resetResult);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "{Operation} 返回 0x75，软复位异常", operation);
        }

        return true;
    }

    #endregion
}