using System;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 日志管线初始化：Serilog + 自定义内存 Sink + Microsoft.Extensions.Logging 桥接
/// </summary>
internal static class LoggingBootstrapper
{
    /// <summary>
    /// 构建日志管线并注册日志相关服务
    /// </summary>
    /// <param name="services">组合根服务集合</param>
    public static void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = new InMemoryLogEntryStore();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SerilogLogEntrySink(store))
            .WriteTo.Console()
            .CreateLogger();

        services.AddSingleton<ILogEntryStore>(store);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        Log.Information("日志系统已就绪，内存缓冲最多保留 {Capacity} 条", store.Capacity);
    }

    /// <summary>
    /// 关闭日志管线并刷新缓冲
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
