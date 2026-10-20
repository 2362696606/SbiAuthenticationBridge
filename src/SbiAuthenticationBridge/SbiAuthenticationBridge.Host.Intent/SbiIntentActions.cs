namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Android SBI Intent 的动作名、extra 键与内容提供者约定
/// </summary>
/// <remarks>
/// <para>
/// MOSIP 的 Android SBI 文档存在两个版本的命名：社区草案使用
/// <c>io.sbi.device</c>、<c>appId.Info</c>、<c>appId.Capture</c>、<c>appId.rCapture</c>；
/// 0.9.5 schema 文档使用 <c>io.mosip.device</c>、<c>appId:Info</c>、<c>appid.capture</c>。
/// 本宿主同时接受两种写法，避免对接方因版本差异无法调用。
/// </para>
/// <para>
/// <see cref="DefaultAppId"/> 即发现响应中的 <c>callbackId</c>，同时也是后续
/// <c>.Info</c> / <c>.Capture</c> 动作的前缀，必须在构建时固定。
/// </para>
/// </remarks>
public static class SbiIntentActions
{
    /// <summary>
    /// 本设备服务对外公布的应用标识，须与 AndroidManifest 中的动作前缀一致
    /// </summary>
    public const string DefaultAppId = "com.CompanyName.SbiAuthenticationBridge";

    /// <summary>
    /// 设备发现动作（社区草案）
    /// </summary>
    public const string Discovery = "io.sbi.device";

    /// <summary>
    /// 设备发现动作（MOSIP 0.9.5 schema 文档）
    /// </summary>
    public const string DiscoveryAlias = "io.mosip.device";

    /// <summary>
    /// 默认应用标识下的设备信息动作
    /// </summary>
    public const string DefaultInfoAction = DefaultAppId + ".Info";

    /// <summary>
    /// 默认应用标识下的设备信息动作（0.9.5 写法）
    /// </summary>
    public const string DefaultInfoAliasAction = DefaultAppId + ":Info";

    /// <summary>
    /// 默认应用标识下的认证采集动作
    /// </summary>
    public const string DefaultCaptureAction = DefaultAppId + ".Capture";

    /// <summary>
    /// 默认应用标识下的认证采集动作（0.9.5 写法）
    /// </summary>
    public const string DefaultCaptureAliasAction = DefaultAppId + ".capture";

    /// <summary>
    /// 默认应用标识下的注册采集动作
    /// </summary>
    public const string DefaultRegistrationCaptureAction = DefaultAppId + ".rCapture";

    /// <summary>
    /// 采集结果的内容提供者 authority
    /// </summary>
    public const string ContentProviderAuthority = DefaultAppId + ".sbi";

    /// <summary>
    /// 请求 JSON 字节数组的 extra 键
    /// </summary>
    public const string InputExtra = "input";

    /// <summary>
    /// 响应（字节数组或 content URI 字符串）的 extra 键
    /// </summary>
    public const string ResponseExtra = "response";

    /// <summary>
    /// 构造指定应用标识的设备信息动作
    /// </summary>
    /// <param name="appId">应用标识</param>
    /// <returns>Intent action</returns>
    public static string Info(string appId) => appId + ".Info";

    /// <summary>
    /// 构造指定应用标识的认证采集动作
    /// </summary>
    /// <param name="appId">应用标识</param>
    /// <returns>Intent action</returns>
    public static string Capture(string appId) => appId + ".Capture";

    /// <summary>
    /// 构造指定应用标识的注册采集动作
    /// </summary>
    /// <param name="appId">应用标识</param>
    /// <returns>Intent action</returns>
    public static string RegistrationCapture(string appId) => appId + ".rCapture";

    /// <summary>
    /// 判断动作是否属于本应用的 SBI 操作
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="appId">应用标识</param>
    /// <param name="operation">解析出的操作</param>
    /// <returns>是否匹配</returns>
    public static bool TryParse(string? action, string appId, out SbiIntentOperation operation)
    {
        operation = default;

        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        var normalized = action.Trim();

        if (string.Equals(normalized, Discovery, StringComparison.Ordinal)
            || string.Equals(normalized, DiscoveryAlias, StringComparison.Ordinal))
        {
            operation = SbiIntentOperation.Discovery;
            return true;
        }

        if (!normalized.StartsWith(appId, StringComparison.OrdinalIgnoreCase)
            || normalized.Length <= appId.Length
            || normalized[appId.Length] is not ('.' or ':'))
        {
            return false;
        }

        // 前缀之后允许 "." 或 ":" 分隔，并忽略大小写（0.9.5 使用小写 capture）
        var suffix = normalized[(appId.Length + 1)..];

        if (string.Equals(suffix, "Info", StringComparison.OrdinalIgnoreCase))
        {
            operation = SbiIntentOperation.Info;
            return true;
        }

        if (string.Equals(suffix, "Capture", StringComparison.OrdinalIgnoreCase))
        {
            operation = SbiIntentOperation.Capture;
            return true;
        }

        if (string.Equals(suffix, "rCapture", StringComparison.OrdinalIgnoreCase))
        {
            operation = SbiIntentOperation.RegistrationCapture;
            return true;
        }

        return false;
    }
}
