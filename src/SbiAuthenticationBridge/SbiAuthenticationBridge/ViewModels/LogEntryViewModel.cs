using System;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Logging;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 单条日志的展示模型
/// </summary>
public sealed class LogEntryViewModel
{
    private static readonly IBrush TraceBrush = new SolidColorBrush(Color.Parse("#808080"));

    private static readonly IBrush DebugBrush = new SolidColorBrush(Color.Parse("#6E7781"));

    private static readonly IBrush InformationBrush = new SolidColorBrush(Color.Parse("#1F6FEB"));

    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#B26A00"));

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C62828"));

    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#8E0000"));

    /// <summary>
    /// 创建日志展示模型
    /// </summary>
    /// <param name="entry">日志记录</param>
    public LogEntryViewModel(LogEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Message = entry.Message;
        ExceptionText = entry.ExceptionText;
        SourceContext = entry.SourceContext;
    }

    /// <summary>
    /// 记录时间
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; }

    /// <summary>
    /// 日志消息
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 异常文本
    /// </summary>
    public string? ExceptionText { get; }

    /// <summary>
    /// 来源类别
    /// </summary>
    public string? SourceContext { get; }

    /// <summary>
    /// 本地时间文本
    /// </summary>
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    /// <summary>
    /// 级别缩写
    /// </summary>
    public string LevelText => Level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    /// <summary>
    /// 级别对应的显示颜色
    /// </summary>
    public IBrush LevelBrush => Level switch
    {
        LogLevel.Trace => TraceBrush,
        LogLevel.Debug => DebugBrush,
        LogLevel.Information => InformationBrush,
        LogLevel.Warning => WarningBrush,
        LogLevel.Error => ErrorBrush,
        LogLevel.Critical => CriticalBrush,
        _ => DebugBrush
    };

    /// <summary>
    /// 是否存在异常文本
    /// </summary>
    public bool HasException => !string.IsNullOrWhiteSpace(ExceptionText);

    /// <summary>
    /// 是否存在来源类别
    /// </summary>
    public bool HasSourceContext => !string.IsNullOrWhiteSpace(SourceContext);

    /// <summary>
    /// 异常文本；无异常时为空字符串
    /// </summary>
    public string ExceptionDisplay => ExceptionText ?? string.Empty;
}
