using Serilog;

namespace SbiAuthenticationBridge;

/// <summary>
/// Serilog 初始化
/// </summary>
/// <remarks>
/// 日志同时写入 <see cref="LogViewSink"/>（界面运行日志）与控制台（便于调试）。
/// 平台启动代码应在创建设备/服务之前调用 <see cref="EnsureConfigured"/>。
/// </remarks>
public static class LoggingSetup
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private static readonly Lock Gate = new();

    private static bool _configured;

    /// <summary>
    /// 初始化 Serilog；可重复调用
    /// </summary>
    public static void EnsureConfigured()
    {
        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Sink(LogViewSink.Instance)
                .WriteTo.Console(outputTemplate: ConsoleTemplate)
                .CreateLogger();

            _configured = true;
        }
    }
}
