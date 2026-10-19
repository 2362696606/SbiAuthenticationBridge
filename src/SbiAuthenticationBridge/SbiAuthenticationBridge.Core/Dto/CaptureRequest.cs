using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集请求：认证采集（<c>/capture</c>）与注册采集（<c>/rcapture</c>）共用
/// </summary>
public class CaptureRequest
{
    /// <summary>
    /// 目标环境
    /// </summary>
    /// <remarks>对应 <c>TargetEnvironment</c>：Staging / Developer / Pre-Production / Production。</remarks>
    [JsonPropertyName("env")]
    public string? Env { get; set; }

    /// <summary>
    /// 用途
    /// </summary>
    /// <remarks>对应 <c>DevicePurpose</c>：Auth 或 Registration；设备未注册时为空。</remarks>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// 期望的 MDS 规范版本
    /// </summary>
    /// <remarks>
    /// 采集请求里是<b>单个字符串</b>；而设备发现与设备信息响应里是版本数组，两处不要混淆。
    /// </remarks>
    [JsonPropertyName("specVersion")]
    public string? SpecVersion { get; set; }

    /// <summary>
    /// 采集超时（毫秒）
    /// </summary>
    /// <remarks>
    /// 应用最多等多久。设备应在超时前交回最好的一帧。毫秒为单位。
    /// </remarks>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    /// <summary>
    /// 采集请求时刻，ISO 格式
    /// </summary>
    /// <remarks>按请求方的时间计。</remarks>
    [JsonPropertyName("captureTime")]
    public string? CaptureTime { get; set; }

    /// <summary>
    /// 认证服务器 URI
    /// </summary>
    /// <remarks>
    /// <b>仅认证采集</b>使用。可用于跨多个提供方/国家/联盟的联邦。
    /// </remarks>
    [JsonPropertyName("domainUri")]
    public string? DomainUri { get; set; }

    /// <summary>
    /// 事务 ID
    /// </summary>
    /// <remarks>
    /// 长度 4..50，只允许字母、数字与连字符；不满足校验时按错误码 112 处理。
    /// 每次请求都应是新的 ID。
    /// </remarks>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    /// <summary>
    /// 要采集的生物特征
    /// </summary>
    [JsonPropertyName("bio")]
    public IReadOnlyList<BioInfo>? Bio { get; set; }

    /// <summary>
    /// 厂商自定义参数
    /// </summary>
    /// <remarks>
    /// 最多 50 个键值对，供设备厂商传入额外参数；取值须由应用服务端配置、可按需修改，
    /// 不得硬编码，且厂商须为其留有文档；<b>不得放入敏感数据</b>。
    /// 值的类型不定，故保留为原始 JSON 值。本设备（FTM）不支持该字段。
    /// </remarks>
    [JsonPropertyName("customOpts")]
    public IReadOnlyDictionary<string, JsonElement>? CustomOpts { get; set; }
}
