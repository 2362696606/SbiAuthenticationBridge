using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// SBI Intent 协议常量与 JSON 设置
/// </summary>
public static class SbiIntentProtocol
{
    /// <summary>
    /// 设备发现 action
    /// </summary>
    public const string DiscoveryAction = "io.sbi.device";

    /// <summary>
    /// 请求数据 extra 键（JSON 的 byte[]）
    /// </summary>
    public const string InputExtra = "input";

    /// <summary>
    /// 响应数据 extra 键：发现/信息为 JSON byte[]，采集为 content URI 字符串
    /// </summary>
    public const string ResponseExtra = "response";

    private const string InfoSuffix = ".Info";
    private const string CaptureSuffix = ".Capture";
    private const string RecaptureSuffix = ".rCapture";

    /// <summary>
    /// 协议 JSON 选项：与 HTTP 宿主保持一致
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 设备信息 action
    /// </summary>
    public static string InfoAction(string appId) => appId + InfoSuffix;

    /// <summary>
    /// 认证采集 action
    /// </summary>
    public static string CaptureAction(string appId) => appId + CaptureSuffix;

    /// <summary>
    /// 注册采集 action
    /// </summary>
    public static string RecaptureAction(string appId) => appId + RecaptureSuffix;
}
