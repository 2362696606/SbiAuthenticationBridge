namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 一次 Intent 请求的处理结果：内联字节或 content URI 内容
/// </summary>
public sealed class SbiIntentResult
{
    private SbiIntentResult(byte[]? inlinePayload, string? contentPayload)
    {
        InlinePayload = inlinePayload;
        ContentPayload = contentPayload;
    }

    /// <summary>
    /// 内联响应的 JSON 字节；采集结果为 <see langword="null"/>
    /// </summary>
    public byte[]? InlinePayload { get; }

    /// <summary>
    /// 采集响应的 JSON 文本；发现/信息结果为 <see langword="null"/>
    /// </summary>
    public string? ContentPayload { get; }

    /// <summary>
    /// 是否以 content URI 回传
    /// </summary>
    public bool IsContent => ContentPayload is not null;

    /// <summary>
    /// 构造内联响应
    /// </summary>
    /// <param name="payload">JSON 字节</param>
    public static SbiIntentResult Inline(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new SbiIntentResult(payload, null);
    }

    /// <summary>
    /// 构造 content URI 响应
    /// </summary>
    /// <param name="json">响应 JSON 文本</param>
    public static SbiIntentResult Content(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new SbiIntentResult(null, json);
    }
}
