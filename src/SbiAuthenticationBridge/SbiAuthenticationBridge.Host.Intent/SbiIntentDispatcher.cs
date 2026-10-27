using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 把 Intent action 分发到认证 ServiceHandle
/// </summary>
internal sealed class SbiIntentDispatcher : ISbiIntentDispatcher
{
    private readonly IServiceProvider _services;

    private readonly SbiIntentHostOptions _options;

    private readonly ILogger<SbiIntentDispatcher> _logger;

    /// <summary>
    /// 创建分发器
    /// </summary>
    /// <param name="services">组合根服务提供者</param>
    /// <param name="options">Intent 宿主配置</param>
    /// <param name="logger">日志</param>
    public SbiIntentDispatcher(
        IServiceProvider services,
        SbiIntentHostOptions options,
        ILogger<SbiIntentDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SbiIntentResult> DispatchAsync(string action, byte[] input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(input);

        var startedAt = Environment.TickCount64;

        try
        {
            var result = await RouteAsync(action, input, cancellationToken).ConfigureAwait(false);
            var elapsedMs = Environment.TickCount64 - startedAt;
            _logger.LogInformation(
                "SBI Intent 处理完成：action={Action}，content={IsContent}，耗时={ElapsedMs} ms",
                action,
                result.IsContent,
                elapsedMs);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SBI Intent 处理失败：action={Action}", action);
            return CreateErrorResult(action, SbiErrorHelper.TechnicalErrorDuringExtraction);
        }
    }

    /// <summary>
    /// 按 action 路由到对应的 ServiceHandle
    /// </summary>
    private Task<SbiIntentResult> RouteAsync(string action, byte[] input, CancellationToken cancellationToken)
    {
        switch (SbiIntentProtocol.Classify(action, _options.AppId))
        {
            case SbiIntentActionKind.Discovery:
                return DiscoverAsync(input, cancellationToken);

            case SbiIntentActionKind.Info:
                return GetDeviceInfoAsync(cancellationToken);

            case SbiIntentActionKind.Capture:
                return CaptureAsync(action, input, cancellationToken);

            default:
                _logger.LogWarning("不支持的 SBI Intent action：{Action}", action);
                return Task.FromResult(
                    CreateErrorResult(action, SbiErrorHelper.TechnicalErrorDuringExtraction));
        }
    }

    private async Task<SbiIntentResult> DiscoverAsync(byte[] input, CancellationToken cancellationToken)
    {
        var request = Deserialize<DiscoveryRequest>(input) ?? new DiscoveryRequest();
        var responses = new List<DiscoveryResponse>();

        responses.AddRange(await _services
            .GetRequiredService<ISbiAuthenticationServiceHandle>()
            .DiscoverAsync(request, cancellationToken)
            .ConfigureAwait(false));

        return SbiIntentResult.Inline(Serialize(responses));
    }

    private async Task<SbiIntentResult> GetDeviceInfoAsync(CancellationToken cancellationToken)
    {
        var responses = new List<DeviceInfoResponse>();

        responses.AddRange(await _services
            .GetRequiredService<ISbiAuthenticationServiceHandle>()
            .GetDeviceInfoAsync(cancellationToken)
            .ConfigureAwait(false));

        return SbiIntentResult.Inline(Serialize(responses));
    }

    private async Task<SbiIntentResult> CaptureAsync(string action, byte[] input,
        CancellationToken cancellationToken)
    {
        if (!IsRunning<ISbiAuthenticationService>())
        {
            return CreateErrorResult(action, SbiErrorHelper.DeviceNotReady);
        }

        var request = Deserialize<CaptureRequest>(input)
            ?? throw new InvalidOperationException("CAPTURE 请求体为空。");

        var response = await _services
            .GetRequiredService<ISbiAuthenticationServiceHandle>()
            .CaptureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return SbiIntentResult.Content(SerializeToText(response));
    }

    /// <summary>
    /// 对应用途的宿主服务是否已启动
    /// </summary>
    private bool IsRunning<TService>()
        where TService : ISbiService =>
        _services.GetService<TService>()?.IsRunning == true;

    /// <summary>
    /// 按 action 生成对应报文结构的 SBI 错误响应
    /// </summary>
    private SbiIntentResult CreateErrorResult(string action, SbiError error) =>
        SbiIntentErrorResponse.Create(action, _options.AppId, error);

    private static T? Deserialize<T>(byte[] input)
        where T : class
    {
        if (input.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(input, SbiIntentProtocol.JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Intent 请求不是合法的 JSON。", exception);
        }
    }

    private static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, SbiIntentProtocol.JsonOptions);

    private static string SerializeToText<T>(T value) =>
        JsonSerializer.Serialize(value, SbiIntentProtocol.JsonOptions);
}
