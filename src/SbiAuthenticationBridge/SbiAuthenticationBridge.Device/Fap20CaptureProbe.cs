namespace SbiAuthenticationBridge.Device;

/// <summary>
/// <c>ftm_capture_auth</c> 的原始返回，用于确认固件实际实现的采集协议
/// </summary>
/// <remarks>
/// 旧固件返回 JSON 文本，FCG-3M115 固件返回 124104 字节、以 <c>FCP1</c> 开头的二进制响应。
/// </remarks>
/// <param name="NativeResult">原生接口返回码；0 表示传输与设备处理均成功</param>
/// <param name="Response">原生接口填充的响应缓冲区</param>
/// <param name="IsFcg3M115">响应是否为 FCG-3M115 FCP1 格式</param>
/// <param name="Text">非 FCP1 响应的 UTF-8 文本；二进制或 FCP1 时为 <c>null</c></param>
public sealed record Fap20CaptureProbe(
    int NativeResult,
    byte[] Response,
    bool IsFcg3M115,
    string? Text)
{
    private const int DescribeTextLimit = 512;

    private const int DescribeHexLimit = 32;

    /// <summary>
    /// 生成适合写入日志或 SBI <c>errorInfo</c> 的单行摘要
    /// </summary>
    public string Describe()
    {
        if (IsFcg3M115)
        {
            return $"FCG-3M115 FCP1 响应（{Response.Length} 字节）";
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var text = Text.Trim();
            return text.Length <= DescribeTextLimit
                ? $"非 FCP1 文本响应：{text}"
                : $"非 FCP1 文本响应：{text[..DescribeTextLimit]}…";
        }

        var length = Math.Min(Response.Length, DescribeHexLimit);
        return $"非 FCP1 二进制响应，前 {length} 字节：{Convert.ToHexString(Response.AsSpan(0, length))}";
    }
}
