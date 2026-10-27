namespace SbiAuthenticationBridge.Android;

/// <summary>
/// Intent 协议使用的应用标识、动作与组件名
/// </summary>
/// <remarks>
/// 取值须与 csproj 中的 <c>ApplicationId</c> 保持一致；action 名称是编译期常量，
/// 供 <c>IntentFilter</c> 特性使用。
/// </remarks>
internal static class SbiIntentAppId
{
    /// <summary>
    /// 应用标识（包名），同时作为 SBI callbackId
    /// </summary>
    public const string Value = "com.CompanyName.SbiAuthenticationBridge";

    /// <summary>
    /// 处理 SBI Intent 的 Activity 名称
    /// </summary>
    public const string ActivityName = Value + ".SbiIntentActivity";

    /// <summary>
    /// 提供采集响应文件的 ContentProvider 名称
    /// </summary>
    public const string ProviderName = Value + ".SbiIntentResponseProvider";

    /// <summary>
    /// 采集响应文件的 authority
    /// </summary>
    public const string ResponseAuthority = Value + ".sbi.responses";

    /// <summary>
    /// 设备信息 action
    /// </summary>
    public const string InfoAction = Value + ".Info";

    /// <summary>
    /// 认证采集 action
    /// </summary>
    public const string CaptureAction = Value + ".Capture";

    /// <summary>
    /// 注册采集 action
    /// </summary>
    public const string RecaptureAction = Value + ".rCapture";
}
