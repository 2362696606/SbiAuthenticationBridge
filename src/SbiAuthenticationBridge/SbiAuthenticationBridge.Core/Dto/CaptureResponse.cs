using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集响应
/// </summary>
public class CaptureResponse
{
    /// <summary>
    /// 本次采集的生物特征条目
    /// </summary>
    /// <remarks>
    /// 一次采集命令对应一条；失败时同样返回一条，只是其中携带 <see cref="BiometricInfo.Error"/>。
    /// </remarks>
    [JsonPropertyName("biometrics")]
    public IReadOnlyList<BiometricInfo>? Biometrics { get; set; }
}
