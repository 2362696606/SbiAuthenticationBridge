using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备信息响应的单条记录：成功时携带 deviceInfo，失败时携带 error
/// </summary>
public class DeviceInfoResponse
{
    /// <summary>
    /// 设备信息的编码字符串
    /// </summary>
    /// <remarks>
    /// 已注册设备返回签名 JWT（<c>header.payload.signature</c>，三段均为 base64url）；
    /// 未注册设备返回未签名的 payload，只有一段 base64url，按 JWT 解析会失败。
    /// 两种形态解码后即得 <c>DeviceInfo</c>。
    /// </remarks>
    [JsonPropertyName("deviceInfo")]
    public string? DeviceInfo { get; set; }

    /// <summary>
    /// 错误信息，仅在出错时存在
    /// </summary>
    [JsonPropertyName("error")]
    public SbiError? Error { get; set; }
}
