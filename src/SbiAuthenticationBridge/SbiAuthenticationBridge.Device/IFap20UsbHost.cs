namespace SbiAuthenticationBridge.Device;

/// <summary>
/// 平台 USB 打开器：负责取得设备访问权并把已打开的句柄交给 <see cref="FAP20"/>
/// </summary>
/// <remarks>
/// Android 上无法直接枚举 <c>/dev/bus/usb</c>，由宿主用 UsbManager 完成授权、
/// ClaimInterface，再通过 <c>ftm_open_fd()</c> 打开；桌面不设置本接口，使用 <c>ftm_open()</c>。
/// </remarks>
public interface IFap20UsbHost
{
    /// <summary>
    /// 打开指定 VID/PID 的 USB 设备
    /// </summary>
    /// <param name="vendorId">USB 厂商 ID</param>
    /// <param name="productId">USB 产品 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 已打开的 USB 会话；设备不存在、未授权或打开失败时返回 <c>null</c>。
    /// </returns>
    Task<IFap20UsbSession?> OpenAsync(ushort vendorId, ushort productId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一次已打开的 USB 会话：SDK 设备句柄与底层 USB 连接的生命周期
/// </summary>
/// <remarks>
/// <see cref="FAP20"/> 关闭时先调用 <c>ftm_close()</c> 释放 <see cref="Handle"/>，
/// 再释放本会话，由实现方关闭 UsbDeviceConnection 等平台资源。
/// </remarks>
public interface IFap20UsbSession : IDisposable
{
    /// <summary>
    /// 已由 <c>ftm_open_fd()</c> 创建的 SDK 设备句柄
    /// </summary>
    IntPtr Handle { get; }
}
