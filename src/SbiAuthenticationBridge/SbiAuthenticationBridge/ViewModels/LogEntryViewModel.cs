using Avalonia.Media;
using SbiAuthenticationBridge.Core.Logging;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 日志列表中的一行
/// </summary>
public sealed class LogEntryViewModel
{
    /// <summary>
    /// 创建行视图模型
    /// </summary>
    /// <param name="entry">运行时日志</param>
    public LogEntryViewModel(LogEntry entry)
    {
        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Category = entry.Category;
        Message = entry.Message;
        ExceptionText = entry.Exception;
    }

    /// <summary>
    /// 产生时间
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 时间文本
    /// </summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss.fff");

    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; }

    /// <summary>
    /// 级别文本
    /// </summary>
    public string LevelText => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    /// <summary>
    /// 级别颜色
    /// </summary>
    public IBrush LevelBrush => Level switch
    {
        LogLevel.Debug => Brushes.Gray,
        LogLevel.Info => Brushes.DodgerBlue,
        LogLevel.Warning => Brushes.Orange,
        LogLevel.Error => Brushes.Red,
        _ => Brushes.Gray
    };

    /// <summary>
    /// 来源分类
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// 日志正文
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 异常完整文本
    /// </summary>
    public string? ExceptionText { get; }

    /// <summary>
    /// 是否包含异常
    /// </summary>
    public bool HasException => !string.IsNullOrEmpty(ExceptionText);
}
