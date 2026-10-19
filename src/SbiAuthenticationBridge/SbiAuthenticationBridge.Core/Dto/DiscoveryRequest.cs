using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备发现（<c>/device</c>）请求
/// </summary>
public class DiscoveryRequest
{
    /// <summary>
    /// 要发现的设备类型
    /// </summary>
    /// <remarks>
    /// 允许 "Biometric Device"、"Finger"、"Face"、"Iris"。
    /// "Biometric Device" 是特殊值，表示"任意生物识别设备"——设备服务应当在
    /// <see cref="Type"/> 与自身类型匹配、或其为该特殊值时才应答。
    /// </remarks>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
