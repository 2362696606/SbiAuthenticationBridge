using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 日志管线初始化：Serilog + 自定义内存 Sink + Microsoft.Extensions.Logging 桥接
/// </summary>
internal static class LoggingBootstrapper
{
    /// <summary>
    /// 单个日志文件的大小上限
    /// </summary>
    private const long FileSizeLimitBytes = 32L * 1024 * 1024;

    /// <summary>
    /// 保留的日志文件数量
    /// </summary>
    private const int RetainedFileCount = 7;

    /// <summary>
    /// 异步日志队列容量
    /// </summary>
    private const int AsyncBufferSize = 10_000;

    /// <summary>
    /// 构建日志管线并注册日志相关服务
    /// </summary>
    /// <param name="services">组合根服务集合</param>
    public static void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = new InMemoryLogEntryStore();
        var logFilePath = Path.Combine(AppContext.BaseDirectory, "Logs", "sbi-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            // 视图内存缓冲只看 Information 及以上，Debug 仅写入文件与控制台
            .WriteTo.Sink(new SerilogLogEntrySink(store), restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.Async(
                sink => sink.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCount,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1)),
                bufferSize: AsyncBufferSize,
                blockWhenFull: false)
            .WriteTo.Console()
            .CreateLogger();

        services.AddSingleton<ILogEntryStore>(store);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        Log.Information(
            "日志系统已就绪，内存缓冲最多保留 {Capacity} 条，文件={LogFilePath}",
            store.Capacity,
            logFilePath);
    }

    /// <summary>
    /// 关闭日志管线并刷新缓冲
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
