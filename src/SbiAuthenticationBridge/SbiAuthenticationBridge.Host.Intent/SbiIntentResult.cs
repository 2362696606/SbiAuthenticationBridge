namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 一次 Intent 调用的结果
/// </summary>
/// <remarks>
/// 设备发现与设备信息的响应放在 intent 的 <c>response</c> extra 中，类型为字节数组；
/// 采集响应按 Android SBI 规范放在 content URI 中，由调用方读取后自行失效。
/// </remarks>
public sealed class SbiIntentResult
{
    private SbiIntentResult(byte[] payload, bool isContentUri)
    {
        Payload = payload;
        IsContentUri = isContentUri;
    }

    /// <summary>
    /// UTF-8 JSON 响应内容
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// 是否应通过 content URI 返回（采集类操作）
    /// </summary>
    public bool IsContentUri { get; }

    /// <summary>
    /// 构造直接放在 intent extra 中的响应
    /// </summary>
    /// <param name="payload">UTF-8 JSON 响应</param>
    /// <returns>Intent 结果</returns>
    public static SbiIntentResult Inline(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new SbiIntentResult(payload, isContentUri: false);
    }

    /// <summary>
    /// 构造通过 content URI 返回的响应
    /// </summary>
    /// <param name="payload">UTF-8 JSON 响应</param>
    /// <returns>Intent 结果</returns>
    public static SbiIntentResult ContentUri(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new SbiIntentResult(payload, isContentUri: true);
    }
}
