using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主服务公共实现：启停状态与 callbackId 写入
/// </summary>
/// <remarks>
/// Intent 由系统投递，服务本身不监听端口；启动仅表示接受请求，
/// 并把 appId 写入 <see cref="ISbiHostContext"/> 作为 callbackId。
/// </remarks>
internal abstract class SbiIntentHostServiceBase : ISbiService
{
    /// <summary>
    /// callbackId 最大长度，对应 DISCOVER/DEVICE_INFO 协议限制
    /// </summary>
    private const int MaxAppIdLength = 192;

    private readonly ISbiHostContext _hostContext;

    private readonly SbiIntentHostOptions _options;

    private readonly ILogger _logger;

    private volatile bool _running;

    /// <summary>
    /// 创建 Intent 宿主服务
    /// </summary>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="options">Intent 宿主配置</param>
    /// <param name="loggerFactory">日志工厂</param>
    protected SbiIntentHostServiceBase(
        ISbiHostContext hostContext,
        SbiIntentHostOptions options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(hostContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _hostContext = hostContext;
        _options = options;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// 本服务对应的设备用途
    /// </summary>
    protected abstract DevicePurpose Purpose { get; }

    /// <summary>
    /// 服务名称，用于日志
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <inheritdoc />
    public bool IsRunning => _running;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            throw new InvalidOperationException($"{ServiceName} 已启动。");
        }

        var appId = ValidateAppId(_options.AppId);
        _hostContext.SetCallbackId(Purpose, appId);
        _running = true;

        _logger.LogInformation("{ServiceName} 已启动，appId={AppId}", ServiceName, appId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running)
        {
            return Task.CompletedTask;
        }

        _running = false;
        _logger.LogInformation("{ServiceName} 已停止", ServiceName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 校验 appId：1..192 个可打印 ASCII 字符
    /// </summary>
    private static string ValidateAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)
            || appId.Length > MaxAppIdLength
            || appId.Any(static c => c is < ' ' or > '~'))
        {
            throw new InvalidOperationException(
                $"Intent 宿主的 appId 非法，须为 1..{MaxAppIdLength} 个可打印 ASCII 字符。");
        }

        return appId;
    }
}
