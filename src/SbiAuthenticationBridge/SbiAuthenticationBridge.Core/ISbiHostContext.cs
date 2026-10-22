namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 宿主上下文：保存随宿主运行状态变化的配置
/// </summary>
/// <remarks>
/// <para>
/// SBI 的 <c>callbackId</c> 由宿主形态决定：Windows/Linux 为 HTTP URL，Android 为 Intent 名称，
/// iOS 为 URL Scheme。HTTP 宿主在端口重绑后取值也会变化，因此不能作为构造入参固化。
/// </para>
/// <para>
/// 宿主在地址确定后写入该值，服务与设备在每次请求时读取当前值。
/// </para>
/// </remarks>
public interface ISbiHostContext
{
    /// <summary>
    /// 宿主对外回调标识，写入 DISCOVER/DEVICE_INFO 请求并随响应返回
    /// </summary>
    /// <remarks>取值须为 1..192 个可打印 ASCII 字符。</remarks>
    string CallbackId { get; set; }
}
