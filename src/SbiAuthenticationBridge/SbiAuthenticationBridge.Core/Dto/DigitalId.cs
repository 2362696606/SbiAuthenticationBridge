using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备数字身份：digitalId 编码字符串解码后的 payload
/// </summary>
public class DigitalId
{
    /// <summary>
    /// 序列号，与 <c>DeviceInfo.deviceCode</c> 相同
    /// </summary>
    [JsonPropertyName("serialNo")]
    public string? SerialNo { get; set; }

    /// <summary>
    /// 品牌名
    /// </summary>
    [JsonPropertyName("make")]
    public string? Make { get; set; }

    /// <summary>
    /// 型号
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// 生物特征类型，对应 <c>BiometricType</c>（Finger / Iris / Face）
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// 设备子类型，对应 <c>DeviceSubType</c>
    /// </summary>
    /// <remarks>
    /// 取值含义随 <see cref="Type"/> 变化：同一取值在不同模态下指不同的设备形态。
    /// </remarks>
    [JsonPropertyName("deviceSubType")]
    public string? DeviceSubType { get; set; }

    /// <summary>
    /// 设备提供商
    /// </summary>
    [JsonPropertyName("deviceProvider")]
    public string? DeviceProvider { get; set; }

    /// <summary>
    /// 设备提供商 ID
    /// </summary>
    [JsonPropertyName("deviceProviderId")]
    public string? DeviceProviderId { get; set; }

    /// <summary>
    /// 签发时间，ISO 格式
    /// </summary>
    [JsonPropertyName("dateTime")]
    public string? DateTime { get; set; }
}
