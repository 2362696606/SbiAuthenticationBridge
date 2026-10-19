using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备预览流（<c>/stream</c>）请求
/// </summary>
/// <remarks>
/// <para>
/// 预览流<b>只在注册环境下可用</b>，且只对用途为 Registration 的设备开放——认证设备的输出是加密的，
/// 拿不出明文帧。故 L1 认证设备上不存在这个接口。
/// </para>
/// <para>
/// 响应不是 JSON，而是 M-JPEG 视频流（≥3 帧/秒）；出错时才回 JSON，见 <see cref="ErrorResponse"/>。
/// </para>
/// </remarks>
public class StreamRequest
{
    /// <summary>
    /// 设备服务内部的设备 ID
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    /// <summary>
    /// 指定设备子 ID
    /// </summary>
    /// <remarks>允许 1、2、3；不知道时为 0（指纹平扫不适用 0）。</remarks>
    [JsonPropertyName("deviceSubId")]
    public int? DeviceSubId { get; set; }

    /// <summary>
    /// 流超时（毫秒）
    /// </summary>
    /// <remarks>可选；到期后流应关闭。缺省为 5 分钟。</remarks>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}
