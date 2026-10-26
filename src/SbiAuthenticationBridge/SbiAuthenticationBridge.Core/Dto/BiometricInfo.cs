using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集响应中 <c>biometrics</c> 数组的一项
/// </summary>
public class BiometricInfo
{
    /// <summary>
    /// 生成本响应用的 MDS 规范版本
    /// </summary>
    /// <remarks>
    /// 这里是<b>单个字符串</b>；设备发现与设备信息响应里才是版本数组。
    /// </remarks>
    [JsonPropertyName("specVersion")]
    public string? SpecVersion { get; set; }

    /// <summary>
    /// 采集数据
    /// </summary>
    /// <remarks>
    /// 线上是 JWT 字符串（<c>header.payload.signature</c>），<b>由设备密钥签名</b>；
    /// 其 payload 解出来是 <see cref="CaptureData"/>。上位机只搬运这个字符串，
    /// 既不解码也不重新签名——改了任何一个字符都会让校验方验不过。
    /// </remarks>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// 哈希链值
    /// </summary>
    /// <remarks>
    /// 大写十六进制：<c>上一个 hash + 本次生物特征 ISO 数据的 SHA-256</c>。供同一事务的下一次采集
    /// 作为 previousHash 回传，从而把各模态、各次采集串成一条链。
    /// </remarks>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>
    /// 会话密钥（仅认证采集）
    /// </summary>
    /// <remarks>
    /// 用 MOSIP 公钥证书按 <c>RSA/ECB/OAEPWithSHA-256AndMGF1Padding</c> 封装后的 base64url 值；
    /// 服务器侧解封后即得解密 <see cref="Data"/> 中 bioValue 所用的对称密钥。
    /// 注册采集不加密，故本字段不存在（为 null 时不写出）。
    /// </remarks>
    [JsonPropertyName("sessionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionKey { get; set; }

    /// <summary>
    /// 服务器证书摘要（仅认证采集）
    /// </summary>
    /// <remarks>
    /// 加密会话密钥所用证书的 SHA-256（大写十六进制、无空格与连字符），服务器据此选用对应私钥。
    /// 注册采集不加密，故本字段不存在（为 null 时不写出）。
    /// </remarks>
    [JsonPropertyName("thumbprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thumbprint { get; set; }

    /// <summary>
    /// 采集数据解码后的内容，供 CTK 等调用方校验
    /// </summary>
    /// <remarks>
    /// 成功采集时由设备层解码 <see cref="Data"/> 填充；错误响应或无法解码时为
    /// <see langword="null"/> 并不写出（CTK 错误 Schema 不允许该字段）。
    /// </remarks>
    [JsonPropertyName("dataDecoded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureDataDecoded? DataDecoded { get; set; }

    /// <summary>
    /// 错误信息，成功时为错误码 <c>0</c>
    /// </summary>
    /// <remarks>
    /// 需要据此判断本次是否采到：<c>101</c> 表示无法检测到生物特征对象（尚未放手指或质量不达标），
    /// 属"再试一次"；<c>109</c> 表示请求的枚数不被支持；<c>112</c> 表示事务 ID 非法。
    /// </remarks>
    [JsonPropertyName("error")]
    public SbiError? Error { get; set; }
}
