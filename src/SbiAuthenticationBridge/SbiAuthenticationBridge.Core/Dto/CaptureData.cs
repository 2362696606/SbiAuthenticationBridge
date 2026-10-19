using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集响应中 <c>data</c> 解码后的载荷
/// </summary>
public class CaptureData
{
    /// <summary>
    /// 设备数字身份，JWT 格式
    /// </summary>
    /// <remarks>L0 设备由 device key 签名；L1 设备由 FTM key 签名。</remarks>
    [JsonPropertyName("digitalId")]
    public string? DigitalId { get; set; }

    /// <summary>
    /// 设备代码，与数字身份中的 serialNo 相同
    /// </summary>
    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    /// <summary>
    /// 设备服务版本
    /// </summary>
    [JsonPropertyName("deviceServiceVersion")]
    public string? DeviceServiceVersion { get; set; }

    /// <summary>
    /// 生物特征类型，对应 <c>BiometricType</c>：Finger / Iris / Face
    /// </summary>
    [JsonPropertyName("bioType")]
    public string? BioType { get; set; }

    /// <summary>
    /// 本次采集的部位
    /// </summary>
    /// <remarks>取值同 <see cref="BioInfo.BioSubType"/>；人脸无本字段。</remarks>
    [JsonPropertyName("bioSubType")]
    public string? BioSubType { get; set; }

    /// <summary>
    /// 用途
    /// </summary>
    /// <remarks>对应 <c>DevicePurpose</c>：Auth 或 Registration。</remarks>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// 目标环境
    /// </summary>
    /// <remarks>对应 <c>TargetEnvironment</c>。</remarks>
    [JsonPropertyName("env")]
    public string? Env { get; set; }

    /// <summary>
    /// 认证服务器 URI
    /// </summary>
    /// <remarks><b>仅认证采集</b>携带。</remarks>
    [JsonPropertyName("domainUri")]
    public string? DomainUri { get; set; }

    /// <summary>
    /// 生物特征数据
    /// </summary>
    /// <remarks>
    /// <para>
    /// 认证采集：生物特征数据（ISO 格式，指纹为 ISO/IEC 19794-4 FIR，内含 WSQ）以随机 256 位
    /// AES 密钥（即会话密钥）加密后，按 base64url 编码。对称加密时以
    /// <c>timestamp XOR transactionId</c> 结果的后 16 字节作 AAD、后 12 字节作 IV（盐）。
    /// </para>
    /// <para>
    /// 注册采集：不做加密，是 base64url 编码的明文 ISO 数据（对应设备侧文件的"明文签名"）。
    /// </para>
    /// </remarks>
    [JsonPropertyName("bioValue")]
    public string? BioValue { get; set; }

    /// <summary>
    /// 事务 ID，与请求中的相同
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    /// <summary>
    /// 采集时刻，ISO 格式
    /// </summary>
    /// <remarks>
    /// 按生物识别设备的时间计。设备应定期从管理服务器同步时间，以保证该时间可信。
    /// </remarks>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// 期望质量分（0-100）
    /// </summary>
    /// <remarks>对应请求中的 requestedScore，按 NFIQ 折算到 0-100。</remarks>
    [JsonPropertyName("requestedScore")]
    public double? RequestedScore { get; set; }

    /// <summary>
    /// 本次采集的实际质量分（0-100）
    /// </summary>
    [JsonPropertyName("qualityScore")]
    public double? QualityScore { get; set; }
}
