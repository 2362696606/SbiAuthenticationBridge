namespace SbiAuthenticationBridge.Core.Logging;

/// <summary>
/// 一条运行时日志
/// </summary>
/// <param name="Timestamp">产生时间</param>
/// <param name="Level">级别</param>
/// <param name="Category">来源分类，如 Host、FAP20、Intent、UI</param>
/// <param name="Message">日志正文</param>
/// <param name="Exception">异常完整文本；无异常时为 null</param>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception = null);
