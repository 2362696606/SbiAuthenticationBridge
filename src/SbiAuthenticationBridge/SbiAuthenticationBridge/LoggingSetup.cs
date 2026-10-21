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

    private static string? _logDirectory;

    /// <summary>
    /// 设置日志文件目录；须在 <see cref="EnsureConfigured"/> 之前调用。
    /// Android 应传入应用私有目录，否则当前目录不可写。
    /// </summary>
    /// <param name="directory">日志目录；为空时只写界面与控制台</param>
    public static void SetLogDirectory(string? directory)
    {
        _logDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

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

            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Sink(LogViewSink.Instance)
                .WriteTo.Console(outputTemplate: ConsoleTemplate);

            if (_logDirectory is { } directory)
            {
                Directory.CreateDirectory(directory);
                configuration.WriteTo.File(Path.Combine(directory, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    shared: true,
                    outputTemplate: ConsoleTemplate);
            }

            Log.Logger = configuration.CreateLogger();
            _configured = true;
        }
    }
}
