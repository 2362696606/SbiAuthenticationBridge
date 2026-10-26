using SbiAuthenticationBridge.Core.Dto.Enum;

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
/// 认证与注册服务各自监听不同端口时，callbackId 也各自独立；宿主在地址确定后按用途写入，
/// 服务与设备在每次请求时读取当前用途的值。
/// </para>
/// </remarks>
public interface ISbiHostContext
{
    /// <summary>
    /// 读取指定用途的宿主回调标识，写入 DISCOVER/DEVICE_INFO 请求并随响应返回
    /// </summary>
    /// <param name="purpose">设备用途（认证 / 注册）</param>
    /// <returns>取值须为 1..192 个可打印 ASCII 字符</returns>
    string GetCallbackId(DevicePurpose purpose);

    /// <summary>
    /// 写入指定用途的宿主回调标识
    /// </summary>
    /// <param name="purpose">设备用途（认证 / 注册）</param>
    /// <param name="callbackId">取值须为 1..192 个可打印 ASCII 字符</param>
    void SetCallbackId(DevicePurpose purpose, string callbackId);
}
