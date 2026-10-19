using System.Diagnostics;
using System.Security.Cryptography;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
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
    /// LOAD_CERTIFICATE 成功响应的固定长度：安装长度 2 字节 + SHA-256 32 字节
    /// </summary>
    private const int LoadCertificateResponseLength = 34;

    /// <summary>
    /// 原生接口不提供响应长度，使用足够大的缓冲区接收回执或错误信息
    /// </summary>
    private const int ResponseBufferSize = 64 * 1024;

    /// <summary>
    /// 证书下发的默认超时：证书走慢速控制传输，默认 5 秒
    /// </summary>
    public const uint DefaultCertificateTimeoutMs = 5_000;

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

    #endregion

    /// <summary>
    /// 创建 FAP20 设备实例
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID，须与 FingerPrintDebug.INI 一致</param>
    /// <param name="productId">USB 产品 ID，须与 FingerPrintDebug.INI 一致</param>
    public FAP20(ushort vendorId, ushort productId)
    {
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
            Debug.WriteLine($"[FAP20] 物理断开后释放设备句柄失败：{exception}");
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 向设备加载域加密证书（LOAD_CERTIFICATE 0x11）
    /// </summary>
    /// <remarks>
    /// 证书以 DER 编码下发，设备安装后会回读安装长度与 SHA-256；只有两者都与请求一致才视为成功。
    /// 证书保存在设备 RAM 中，设备断开或复位后需要重新加载。
    /// </remarks>
    /// <param name="certificateDer">DER 编码的 X.509 证书，长度为 1..3500 字节</param>
    /// <param name="timeoutMs">下发超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备回读的证书 SHA-256（大写十六进制）</returns>
    /// <exception cref="ArgumentException">证书为空或超过协议允许的最大长度</exception>
    /// <exception cref="InvalidOperationException">设备未连接、下发失败或回读校验不一致</exception>
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

        return Task.Run(() =>
        {
            var response = ExecuteRequest("ftm_load_certificate", request, timeoutMs,
                FtmNativeMethods.LoadCertificate);

            return VerifyLoadCertificateResponse(
                response.AsSpan(0, LoadCertificateResponseLength), der);
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
        RequestCommand command)
    {
        var response = new byte[ResponseBufferSize];

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
            Debug.WriteLine($"[FAP20] {operation} 返回 0x75，但设备句柄为空，跳过软复位。");
            return false;
        }

        try
        {
            var resetResult = FtmNativeMethods.DeviceReset(handle, AutoResetTimeoutMs);

            Debug.WriteLine(resetResult == 0
                ? $"[FAP20] {operation} 返回 0x75，已执行软复位。"
                : $"[FAP20] {operation} 返回 0x75，软复位失败，reset rc={resetResult}。");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[FAP20] {operation} 返回 0x75，软复位异常：{exception}");
        }

        return true;
    }

    /// <summary>
    /// 校验设备回执中的安装长度与 SHA-256，返回设备回读的证书摘要
    /// </summary>
    private static string VerifyLoadCertificateResponse(ReadOnlySpan<byte> response, ReadOnlySpan<byte> der)
    {
        var installedLength = (response[0] << 8) | response[1];
        if (installedLength == 0)
        {
            throw new InvalidOperationException("设备未返回证书安装回执，无法确认证书已加载。");
        }

        if (installedLength != der.Length)
        {
            throw new InvalidOperationException(
                $"证书加载长度不一致：请求 {der.Length} 字节，设备回读 {installedLength} 字节。");
        }

        var returnedHash = response.Slice(2, 32);
        var expectedHash = SHA256.HashData(der);
        if (!returnedHash.SequenceEqual(expectedHash))
        {
            throw new InvalidOperationException(
                $"证书加载校验失败：设备回读 SHA-256 {Convert.ToHexString(returnedHash)}，" +
                $"期望 {Convert.ToHexString(expectedHash)}。");
        }

        return Convert.ToHexString(returnedHash);
    }

    #endregion
}
