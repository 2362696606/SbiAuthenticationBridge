using Microsoft.Extensions.Logging;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 日志级别过滤项
/// </summary>
/// <param name="minimumLevel">显示的最小级别</param>
/// <param name="displayName">显示名称</param>
public sealed class LogLevelOption(LogLevel minimumLevel, string displayName)
{
    /// <summary>
    /// 显示的最小级别
    /// </summary>
    public LogLevel MinimumLevel { get; } = minimumLevel;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; } = displayName;
}
