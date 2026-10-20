using System.Collections.Concurrent;
using System.Globalization;
using SbiAuthenticationBridge.Core.Logging;
using Serilog.Core;
using Serilog.Events;

namespace SbiAuthenticationBridge;

/// <summary>
/// 把 Serilog 日志写入运行日志视图的 Sink
/// </summary>
/// <remarks>
/// <para>
/// 设备与宿主服务会从后台线程写日志，因此使用并发队列暂存；<see cref="ViewModels.LogViewModel"/>
/// 在 UI 线程定时 <see cref="Drain"/> 并展示。
/// </para>
/// <para>
/// 队列在界面未创建或未及时取出时最多保留 <see cref="MaxPending"/> 条，避免无限增长。
/// </para>
/// </remarks>
public sealed class LogViewSink : ILogEventSink
{
    private const int MaxPending = 5000;

    private readonly ConcurrentQueue<LogEntry> _pending = new();

    /// <summary>
    /// 全局唯一 Sink 实例
    /// </summary>
    public static LogViewSink Instance { get; } = new();

    private LogViewSink()
    {
    }

    /// <summary>
    /// 接收 Serilog 日志事件
    /// </summary>
    /// <param name="logEvent">日志事件</param>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var entry = new LogEntry(
            logEvent.Timestamp,
            MapLevel(logEvent.Level),
            GetSourceContext(logEvent),
            logEvent.RenderMessage(CultureInfo.InvariantCulture),
            logEvent.Exception?.ToString());

        while (_pending.Count >= MaxPending && _pending.TryDequeue(out _))
        {
        }

        _pending.Enqueue(entry);
    }

    /// <summary>
    /// 取出当前全部待展示日志
    /// </summary>
    /// <returns>按写入顺序排列的日志</returns>
    public IReadOnlyList<LogEntry> Drain()
    {
        if (_pending.IsEmpty)
        {
            return [];
        }

        var entries = new List<LogEntry>();

        while (_pending.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// 清空尚未展示的日志
    /// </summary>
    public void ClearPending()
    {
        while (_pending.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// 把 Serilog 级别映射为界面级别
    /// </summary>
    private static LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Info,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error or LogEventLevel.Fatal => LogLevel.Error,
        _ => LogLevel.Info
    };

    /// <summary>
    /// 读取 <c>SourceContext</c> 作为分类
    /// </summary>
    private static string GetSourceContext(LogEvent logEvent)
    {
        return logEvent.Properties.TryGetValue("SourceContext", out var value)
               && value is ScalarValue { Value: string sourceContext }
            ? sourceContext
            : "App";
    }
}
