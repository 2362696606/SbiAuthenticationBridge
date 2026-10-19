using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// MDS 设备服务返回的错误信息，对应报文中的 error 对象
/// </summary>
public class SbiError
{
    /// <summary>
    /// 标准错误码，报文中的 JSON 值为字符串
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 面向终端用户展示的错误描述，支持多语言
    /// </summary>
    [JsonPropertyName("errorInfo")]
    public string? ErrorInfo { get; set; }
}
