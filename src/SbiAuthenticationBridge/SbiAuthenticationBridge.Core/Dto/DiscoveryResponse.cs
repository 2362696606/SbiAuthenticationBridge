using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备发现（<c>/device</c>）响应的单条记录
/// </summary>
public class DiscoveryResponse
{
    /// <summary>
    /// 设备服务内部的设备 ID
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    /// <summary>
    /// 设备状态
    /// </summary>
    /// <remarks>
    /// 对应 <c>DeviceStatus</c>：Ready / Busy / Not Ready / Not Registered。
    /// 处于 Busy、Not Ready、Not Registered 时，须同时按错误码表给出错误。
    /// </remarks>
    [JsonPropertyName("deviceStatus")]
    public string? DeviceStatus { get; set; }

    /// <summary>
    /// 认证等级
    /// </summary>
    /// <remarks>
    /// 对应 <c>CertificationLevel</c>：L0 / L1。
    /// </remarks>
    [JsonPropertyName("certification")]
    public string? Certification { get; set; }

    /// <summary>
    /// 设备服务版本
    /// </summary>
    [JsonPropertyName("serviceVersion")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// 支持的设备子 ID 数组
    /// </summary>
    /// <remarks>
    /// 允许 1、2、3：指纹/虹膜中 1 为左、2 为右、3 为双拇指/双眼；
    /// 不知道具体子 ID 时为 0（指纹平扫不适用 0）。子 ID 从 1 起按存在的子设备顺序递增。
    /// </remarks>
    [JsonPropertyName("deviceSubId")]
    public IReadOnlyList<int>? DeviceSubId { get; set; }

    /// <summary>
    /// 回调地址
    /// </summary>
    /// <remarks>
    /// 随操作系统不同：Linux/Windows 为 HTTP URL，Android 为 Intent 名，iOS 为 URL Scheme。
    /// 该地址将作为后续请求的基础地址，优先于此前的回调地址。
    /// </remarks>
    [JsonPropertyName("callbackId")]
    public string? CallbackId { get; set; }

    /// <summary>
    /// 设备数字身份，此处不签名
    /// </summary>
    [JsonPropertyName("digitalId")]
    public string? DigitalId { get; set; }

    /// <summary>
    /// 设备代码，与数字身份中的 serialNo 相同
    /// </summary>
    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    /// <summary>
    /// 支持的 MDS 规范版本数组
    /// </summary>
    /// <remarks>第 0 个元素必须是生成当前响应用的规范版本。</remarks>
    [JsonPropertyName("specVersion")]
    public IReadOnlyList<string>? SpecVersion { get; set; }

    /// <summary>
    /// 设备在 MOSIP 中的用途
    /// </summary>
    /// <remarks>
    /// 对应 <c>DevicePurpose</c>：Auth 或 Registration；未注册时为空。
    /// </remarks>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// 错误信息，仅在出错时存在
    /// </summary>
    [JsonPropertyName("error")]
    public SbiError? Error { get; set; }
}
