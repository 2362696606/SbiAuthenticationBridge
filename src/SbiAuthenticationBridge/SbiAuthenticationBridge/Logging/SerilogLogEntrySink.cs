using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 把 Serilog 事件写入内存缓冲的自定义 Sink
/// </summary>
/// <param name="store">日志内存缓冲</param>
internal sealed class SerilogLogEntrySink(ILogEntryStore store) : ILogEventSink
{
    private readonly ILogEntryStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        try
        {
            _store.Append(new LogEntryDto(
                logEvent.Timestamp,
                MapLevel(logEvent.Level),
                logEvent.RenderMessage(),
                logEvent.Exception?.ToString(),
                TryGetSourceContext(logEvent)));
        }
        catch (Exception exception)
        {
            // 日志查看失败不能反向影响日志调用方，仅输出到调试窗口
            Debug.WriteLine($"写入内存日志缓冲失败：{exception}");
        }
    }

    /// <summary>
    /// 把 Serilog 级别映射为 Microsoft.Extensions.Logging 级别
    /// </summary>
    private static LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.None
    };

    /// <summary>
    /// 读取日志来源类别
    /// </summary>
    private static string? TryGetSourceContext(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value)
        && value is ScalarValue { Value: string sourceContext }
            ? sourceContext
            : null;
}
