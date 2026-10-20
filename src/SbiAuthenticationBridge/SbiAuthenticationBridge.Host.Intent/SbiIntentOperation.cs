namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主支持的 SBI 操作
/// </summary>
public enum SbiIntentOperation
{
    /// <summary>
    /// 设备发现（<c>io.sbi.device</c> / <c>io.mosip.device</c>）
    /// </summary>
    Discovery,

    /// <summary>
    /// 设备信息（<c>appId.Info</c> / <c>appId:Info</c>）
    /// </summary>
    Info,

    /// <summary>
    /// 认证采集（<c>appId.Capture</c> / <c>appId.capture</c>）
    /// </summary>
    Capture,

    /// <summary>
    /// 注册采集（<c>appId.rCapture</c>）
    /// </summary>
    RegistrationCapture
}
