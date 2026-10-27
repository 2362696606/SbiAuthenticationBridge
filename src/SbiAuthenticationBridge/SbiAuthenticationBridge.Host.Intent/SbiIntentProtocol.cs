using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// SBI Intent 协议常量与 JSON 设置
/// </summary>
/// <remarks>
/// action 采用 MOSIP SBI 0.9.5 Request/Response 规范：
/// <c>io.mosip.device</c>、<c>appId:Info</c>、<c>appid.capture</c>。
/// 信息/采集同时兼容草案里的 <c>appId.Info</c> / <c>appId.Capture</c> 写法。
/// </remarks>
public static class SbiIntentProtocol
{
    /// <summary>
    /// 设备发现 action
    /// </summary>
    public const string DiscoveryAction = "io.mosip.device";

    /// <summary>
    /// 设备发现 action 草案写法；CTK 扫描使用该值
    /// </summary>
    public const string DiscoveryCompatAction = "io.sbi.device";

    /// <summary>
    /// 请求数据 extra 键（JSON 的 byte[]）
    /// </summary>
    public const string InputExtra = "input";

    /// <summary>
    /// 响应数据 extra 键：发现/信息为 JSON byte[]，采集为 content URI 字符串
    /// </summary>
    public const string ResponseExtra = "response";

    private const string InfoSeparator = ":";
    private const string InfoName = "Info";
    private const string InfoSuffix = ".Info";
    private const string CaptureSuffix = ".capture";

    /// <summary>
    /// 协议 JSON 选项：与 HTTP 宿主保持一致
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 设备信息 action（<c>appId:Info</c>）
    /// </summary>
    public static string InfoAction(string appId) => appId + InfoSeparator + InfoName;

    /// <summary>
    /// 认证采集 action（<c>appId.capture</c>）
    /// </summary>
    public static string CaptureAction(string appId) => appId + CaptureSuffix;

    /// <summary>
    /// 归类 action，大小写不敏感，兼容两种分隔符/大小写写法
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="appId">Intent 宿主 appId</param>
    /// <returns>action 类型</returns>
    internal static SbiIntentActionKind Classify(string action, string appId)
    {
        if (string.Equals(action, DiscoveryAction, StringComparison.Ordinal)
            || string.Equals(action, DiscoveryCompatAction, StringComparison.Ordinal))
        {
            return SbiIntentActionKind.Discovery;
        }

        if (IsAppAction(action, appId, InfoSeparator + InfoName)
            || IsAppAction(action, appId, InfoSuffix))
        {
            return SbiIntentActionKind.Info;
        }

        if (IsAppAction(action, appId, CaptureSuffix))
        {
            return SbiIntentActionKind.Capture;
        }

        return SbiIntentActionKind.Unknown;
    }

    private static bool IsAppAction(string action, string appId, string suffix) =>
        action.Length == appId.Length + suffix.Length
        && action.StartsWith(appId, StringComparison.OrdinalIgnoreCase)
        && action.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// SBI Intent action 类型
/// </summary>
internal enum SbiIntentActionKind
{
    /// <summary>
    /// 未知 action
    /// </summary>
    Unknown,

    /// <summary>
    /// 设备发现
    /// </summary>
    Discovery,

    /// <summary>
    /// 设备信息
    /// </summary>
    Info,

    /// <summary>
    /// 认证采集
    /// </summary>
    Capture,
}
