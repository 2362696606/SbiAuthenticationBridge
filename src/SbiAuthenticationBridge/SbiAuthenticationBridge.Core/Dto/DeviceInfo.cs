using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// MDS 设备信息：deviceInfo 编码字符串解码后的 payload
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// 设备状态
    /// </summary>
    /// <remarks>
    /// 对应 <c>DeviceStatus</c>：Ready / Busy / Not Ready / Not Registered。
    /// 处于 Busy、Not Ready、Not Registered 时，设备应同时返回相应错误码。
    /// </remarks>
    [JsonPropertyName("deviceStatus")]
    public string? DeviceStatus { get; set; }

    /// <summary>
    /// 设备在设备服务内部的 ID，用于标识真实的生物识别设备
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    /// <summary>
    /// 固件版本
    /// </summary>
    /// <remarks>
    /// L0 设备与 <see cref="ServiceVersion"/> 相同。
    /// </remarks>
    [JsonPropertyName("firmware")]
    public string? Firmware { get; set; }

    /// <summary>
    /// 认证等级
    /// </summary>
    /// <remarks>
    /// 对应 <c>CertificationLevel</c>：L0 / L1。二者区别在加密执行的位置与信任基础，
    /// 而非采集质量高低——L0 由主机侧的设备驱动或 MDS 加密，信任基于软件；
    /// L1 在设备可信区内加密，信任基于带安全执行环境的安全芯片与已认证 FTM。
    /// </remarks>
    [JsonPropertyName("certification")]
    public string? Certification { get; set; }

    /// <summary>
    /// 设备服务版本
    /// </summary>
    /// <remarks>
    /// 规范中字段说明写作"设备服务版本"，取值表却写作"所支持的 MDS 规范版本"，二者不一致；
    /// 落地前需与实际对接方的取值核对，勿与 <see cref="SpecVersion"/> 混淆。
    /// </remarks>
    [JsonPropertyName("serviceVersion")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// 设备子 ID 数组，用于选择扫描器模块
    /// </summary>
    /// <remarks>
    /// 指纹/虹膜中 1 为左、2 为右、3 为双侧（双拇指/双虹膜）；未知或平扫不适用时可为 0。
    /// 规范里是数字索引而非字符串枚举，故保持 <see cref="int"/>。
    /// </remarks>
    [JsonPropertyName("deviceSubId")]
    public IReadOnlyList<int>? DeviceSubId { get; set; }

    /// <summary>
    /// 回调地址，后续请求以它为基础地址
    /// </summary>
    /// <remarks>
    /// Linux/Windows 为 HTTP URL，Android 为 Intent 名称，iOS 为 URL Scheme。
    /// </remarks>
    [JsonPropertyName("callbackId")]
    public string? CallbackId { get; set; }

    /// <summary>
    /// 设备数字身份（编码字符串，解码后见 <c>DigitalId</c>）
    /// </summary>
    /// <remarks>
    /// 形态与 deviceInfo 本身相同：已注册设备为签名 JWT，未注册设备为 base64url 编码的 payload。
    /// 未注册的 L0 设备不签名；已注册的 L0 由 device key 签名；L1 由 FTM key 签名。
    /// </remarks>
    [JsonPropertyName("digitalId")]
    public string? DigitalId { get; set; }

    /// <summary>
    /// 设备代码，须与 <see cref="DigitalId"/> 中的 serialNo 相同
    /// </summary>
    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    /// <summary>
    /// 目标环境
    /// </summary>
    /// <remarks>
    /// 对应 <c>TargetEnvironment</c>：未注册时为 None，
    /// 已注册时为 Staging / Developer / Pre-Production / Production。
    /// </remarks>
    [JsonPropertyName("env")]
    public string? Env { get; set; }

    /// <summary>
    /// 设备在 MOSIP 中的用途
    /// </summary>
    /// <remarks>
    /// 对应 <c>DevicePurpose</c>：未注册时为空，已注册时为 Auth 或 Registration。
    /// </remarks>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// 支持的 MDS 规范版本数组
    /// </summary>
    /// <remarks>
    /// 第一个元素必须是生成当前响应所用的规范版本。
    /// </remarks>
    [JsonPropertyName("specVersion")]
    public IReadOnlyList<string>? SpecVersion { get; set; }
}
