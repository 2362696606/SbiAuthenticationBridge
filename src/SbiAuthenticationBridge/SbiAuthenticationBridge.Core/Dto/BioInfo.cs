using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 采集请求里 <c>bio</c> 数组的一项
/// </summary>
public class BioInfo
{
    /// <summary>
    /// 生物特征类型，对应 <c>BiometricType</c>：Finger / Iris / Face
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// 本次要采集的枚数
    /// </summary>
    /// <remarks>
    /// 人脸恒为 1；指纹/虹膜应等于 <see cref="BioSubType"/> 的个数。设备须校验它与实际采集一致，
    /// 不一致时按错误码 109 处理。
    /// </remarks>
    [JsonPropertyName("count")]
    public int? Count { get; set; }

    /// <summary>
    /// 具名部位数组
    /// </summary>
    /// <remarks>
    /// 指纹："Left IndexFinger"、"Left MiddleFinger"、"Left RingFinger"、"Left LittleFinger"、
    /// "Left Thumb"、"Right IndexFinger"、"Right MiddleFinger"、"Right RingFinger"、
    /// "Right LittleFinger"、"Right Thumb"、"UNKNOWN"；
    /// 虹膜："Left"、"Right"、"UNKNOWN"；人脸不发本字段。
    /// </remarks>
    [JsonPropertyName("bioSubType")]
    public IReadOnlyList<string>? BioSubType { get; set; }

    /// <summary>
    /// 要排除的指纹或虹膜部位
    /// </summary>
    /// <remarks>
    /// <b>仅注册采集</b>使用（认证采集不发本字段）。指纹取值同 <see cref="BioSubType"/>（不含 UNKNOWN），
    /// 虹膜为 "Left"、"Right"。当 <see cref="Type"/> 为 Face 时，本字段用于标注随人脸一并采集的异常照片。
    /// </remarks>
    [JsonPropertyName("exception")]
    public IReadOnlyList<string>? Exception { get; set; }

    /// <summary>
    /// 期望质量分
    /// </summary>
    /// <remarks>
    /// 达到该分数时设备应自动采集；到超时仍未达标，则交回采集过程中最好的一帧。
    /// 取值范围 0-100（指纹按 NFIQ 折算；注册采集按 NFIQ v2.0 计分，多指时取平均值）。
    /// </remarks>
    [JsonPropertyName("requestedScore")]
    public double? RequestedScore { get; set; }

    /// <summary>
    /// 设备服务内部的设备 ID
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    /// <summary>
    /// 指定设备子 ID
    /// </summary>
    /// <remarks>允许 1、2、3；不知道时为 0（指纹平扫不适用 0）。</remarks>
    [JsonPropertyName("deviceSubId")]
    public int? DeviceSubId { get; set; }

    /// <summary>
    /// 上一个哈希链值
    /// </summary>
    /// <remarks>
    /// 首次采集为 SHA-256 空输入摘要，其后为上一次采集返回的 <c>hash</c>。
    /// 用于把同一事务、同一时间窗内的各模态采集串成一条链。
    /// </remarks>
    [JsonPropertyName("previousHash")]
    public string? PreviousHash { get; set; }
}
