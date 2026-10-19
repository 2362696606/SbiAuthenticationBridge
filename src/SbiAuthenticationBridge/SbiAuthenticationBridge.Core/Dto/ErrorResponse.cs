using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 只携带错误信息的响应体
/// </summary>
/// <remarks>
/// 预览流出错时就是这个形状：正常时响应是 M-JPEG 视频流而非 JSON，报了 JSON 就说明流没起来。
/// </remarks>
public class ErrorResponse
{
    /// <summary>
    /// 错误信息
    /// </summary>
    [JsonPropertyName("error")]
    public SbiError? Error { get; set; }
}
