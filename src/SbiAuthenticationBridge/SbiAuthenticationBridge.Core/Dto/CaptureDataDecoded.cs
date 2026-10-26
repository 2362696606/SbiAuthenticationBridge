using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集响应中 <c>data</c> 解码后的内容
/// </summary>
/// <remarks>
/// 与 CTK 0.9.5 的 <c>dataDecoded</c> Schema 对齐；Auth 与 Registration 共用，
/// 通过 <see cref="Purpose"/> 与是否加密区分。
/// </remarks>
public sealed class CaptureDataDecoded
{
    /// <summary>
    /// 设备数字身份编码字符串
    /// </summary>
    [JsonPropertyName("digitalId")]
    public string? DigitalId { get; set; }

    /// <summary>
    /// 数字身份解码后的内容
    /// </summary>
    [JsonPropertyName("digitalIdDecoded")]
    public DigitalId? DigitalIdDecoded { get; set; }

    /// <summary>
    /// 设备代码
    /// </summary>
    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    /// <summary>
    /// 设备服务版本
    /// </summary>
    [JsonPropertyName("deviceServiceVersion")]
    public string? DeviceServiceVersion { get; set; }

    /// <summary>
    /// 生物特征类型：Finger / Iris / Face
    /// </summary>
    [JsonPropertyName("bioType")]
    public string? BioType { get; set; }

    /// <summary>
    /// 本次采集的部位
    /// </summary>
    [JsonPropertyName("bioSubType")]
    public string? BioSubType { get; set; }

    /// <summary>
    /// 用途：Auth / Registration
    /// </summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// 目标环境
    /// </summary>
    [JsonPropertyName("env")]
    public string? Env { get; set; }

    /// <summary>
    /// 认证服务器 URI
    /// </summary>
    /// <remarks>
    /// 认证采集必须提供（可为空串）；注册采集不写该字段——CTK Registration Schema 不允许
    /// <c>domainUri</c>，注册实现须保持为 <see langword="null"/> 以便省略。
    /// </remarks>
    [JsonPropertyName("domainUri")]
    public string? DomainUri { get; set; }

    /// <summary>
    /// 生物特征数据（认证采集为加密值，注册采集为明文）
    /// </summary>
    [JsonPropertyName("bioValue")]
    public string? BioValue { get; set; }

    /// <summary>
    /// 事务 ID
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    /// <summary>
    /// 采集时刻，ISO 格式
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// 期望质量分；CTK Schema 要求字符串
    /// </summary>
    [JsonPropertyName("requestedScore")]
    public string? RequestedScore { get; set; }

    /// <summary>
    /// 实际质量分；CTK Schema 要求字符串
    /// </summary>
    [JsonPropertyName("qualityScore")]
    public string? QualityScore { get; set; }
}
