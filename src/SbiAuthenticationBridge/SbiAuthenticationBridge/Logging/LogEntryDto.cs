using System;
using Microsoft.Extensions.Logging;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 一条日志记录，供内存缓冲与日志视图使用
/// </summary>
/// <param name="Timestamp">记录时间</param>
/// <param name="Level">日志级别</param>
/// <param name="Message">渲染后的消息</param>
/// <param name="ExceptionText">异常完整文本；无异常时为 <see langword="null"/></param>
/// <param name="SourceContext">来源类别；未提供时为 <see langword="null"/></param>
public sealed record LogEntryDto(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message,
    string? ExceptionText,
    string? SourceContext);
