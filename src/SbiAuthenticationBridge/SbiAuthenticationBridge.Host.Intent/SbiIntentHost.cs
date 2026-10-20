using System.Text.Json;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using Serilog;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Android SBI Intent 宿主：把 Intent 的 action 与 <c>input</c> 字节转换为服务调用，
/// 并把响应序列化为 <c>response</c> 所需的内容。
/// </summary>
/// <remarks>
/// <para>
/// 本类不依赖 Android 类型，便于在桌面环境做单元测试；Android 的 Activity 与
/// ContentProvider 只负责收发 Intent 和 content URI。
/// </para>
/// <para>
/// 服务实现通过构造函数注入：认证与注册服务可同时提供，设备发现/信息查询默认复用
/// 认证服务（或注册服务）的实现。
/// </para>
/// <para>
/// 宿主实现 <see cref="ISbiService"/>：主应用可调用 <see cref="StartAsync"/> /
/// <see cref="StopAsync"/> 启停 Intent 服务。停止后不再处理新请求，并取消进行中的请求；
/// 再次启动即可恢复。
/// </para>
/// </remarks>
public sealed class SbiIntentHost : ISbiService, ISbiServiceState
{
    private const int InternalErrorCode = 500;

    private const int ServiceUnavailableErrorCode = 503;

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Intent");

    private readonly ISbiServiceHandle _sbiService;

    private readonly IAuthenticationServiceHandle? _authenticationService;

    private readonly IRegisterSbiServiceHandle? _registrationService;

    private readonly Lock _lifecycleGate = new();

    private CancellationTokenSource _lifetime = new();

    private bool _running;

    /// <summary>
    /// 创建 Intent 宿主
    /// </summary>
    /// <param name="sbiService">设备发现/信息查询服务；为空时依次复用认证、注册服务</param>
    /// <param name="authenticationService">认证采集服务</param>
    /// <param name="registrationService">注册采集服务</param>
    /// <param name="appId">
    /// 对外公布的应用标识，须与 Intent action 前缀一致；为空时使用
    /// <see cref="SbiIntentActions.DefaultAppId"/>
    /// </param>
    public SbiIntentHost(ISbiServiceHandle? sbiService = null,
        IAuthenticationServiceHandle? authenticationService = null,
        IRegisterSbiServiceHandle? registrationService = null,
        string? appId = null)
    {
        _authenticationService = authenticationService;
        _registrationService = registrationService;
        _sbiService = sbiService
                      ?? authenticationService
                      ?? (ISbiServiceHandle?)registrationService
                      ?? NullSbiServiceHandle.Instance;

        AppId = string.IsNullOrWhiteSpace(appId) ? SbiIntentActions.DefaultAppId : appId.Trim();
    }

    /// <summary>
    /// 本设备服务对外公布的应用标识（即发现响应中的 callbackId）
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// 启动 Intent 服务
    /// </summary>
    /// <returns>启动任务；可重复调用</returns>
    public Task StartAsync()
    {
        lock (_lifecycleGate)
        {
            if (_running)
            {
                return Task.CompletedTask;
            }

            // 上次停止时已取消，重新启动需要一个新的生命周期令牌
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            _running = true;
        }

        Logger.Information("SBI 服务已启动");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止 Intent 服务：拒绝新请求并取消进行中的请求
    /// </summary>
    /// <returns>停止任务；可重复调用</returns>
    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            if (!_running)
            {
                return Task.CompletedTask;
            }

            _running = false;
            _lifetime.Cancel();
        }

        Logger.Information("SBI 服务已停止");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理一次 Intent 调用
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="input"><c>input</c> extra 中的 UTF-8 JSON 字节</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>要写回 <c>response</c> extra 的结果</returns>
    /// <exception cref="OperationCanceledException">调用方取消</exception>
    public async Task<SbiIntentResult> HandleAsync(string? action, byte[]? input,
        CancellationToken cancellationToken = default)
    {
        if (!SbiIntentActions.TryParse(action, AppId, out var operation))
        {
            Logger.Warning("未知的 SBI Intent 动作：{Action:l}", action);
            return SbiIntentResult.Inline(
                SerializeError(SbiErrorHelper.Custom(InternalErrorCode, "Unknown SBI intent action")));
        }

        Logger.Debug("处理 SBI Intent 请求：{Action:l}", action);

        var isCapture = operation is SbiIntentOperation.Capture
            or SbiIntentOperation.RegistrationCapture;

        // 把调用方的取消令牌与服务生命周期令牌合并：StopAsync 会取消进行中的请求
        using var linkedCancellation = CreateLinkedCancellation(cancellationToken);

        if (linkedCancellation is null)
        {
            Logger.Warning("SBI 服务未运行，拒绝 Intent 请求：{Action:l}", action);
            return CreateErrorResult(
                SbiErrorHelper.Custom(ServiceUnavailableErrorCode, "SBI service is not running"),
                isCapture);
        }

        var token = linkedCancellation.Token;

        try
        {
            return operation switch
            {
                SbiIntentOperation.Discovery => await HandleDiscoveryAsync(input, token)
                    .ConfigureAwait(false),
                SbiIntentOperation.Info => await HandleInfoAsync(token)
                    .ConfigureAwait(false),
                SbiIntentOperation.Capture => await HandleCaptureAsync(input, isRegistration: false,
                    token).ConfigureAwait(false),
                SbiIntentOperation.RegistrationCapture => await HandleCaptureAsync(input,
                    isRegistration: true, token).ConfigureAwait(false),
                _ => SbiIntentResult.Inline(
                    SerializeError(SbiErrorHelper.Custom(InternalErrorCode, "Unsupported SBI operation")))
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SbiException exception)
        {
            return CreateErrorResult(exception.Error, isCapture);
        }
        catch (JsonException exception)
        {
            Logger.Warning(exception, "请求 JSON 解析失败");
            return CreateErrorResult(InvalidRequest(), isCapture);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "处理 SBI Intent 失败");
            return CreateErrorResult(
                SbiErrorHelper.Custom(InternalErrorCode, "Internal error"), isCapture);
        }
    }

    /// <summary>
    /// 在服务运行状态下创建“调用方取消令牌 + 服务生命周期令牌”的合并令牌
    /// </summary>
    /// <param name="cancellationToken">调用方取消令牌</param>
    /// <returns>合并令牌；服务已停止时返回 <c>null</c></returns>
    private CancellationTokenSource? CreateLinkedCancellation(CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            return _running
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token)
                : null;
        }
    }

    /// <summary>
    /// 处理设备发现
    /// </summary>
    private async Task<SbiIntentResult> HandleDiscoveryAsync(byte[]? input,
        CancellationToken cancellationToken)
    {
        DiscoveryRequest request;

        try
        {
            request = SbiIntentJson.Deserialize<DiscoveryRequest>(input) ?? new DiscoveryRequest();
        }
        catch (JsonException exception)
        {
            Logger.Warning(exception, "设备发现请求解析失败");
            return SbiIntentResult.Inline(SbiIntentJson.Serialize(
                new[] { new DiscoveryResponse { Error = InvalidRequest() } }));
        }

        var responses = await _sbiService.DiscoverAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return SbiIntentResult.Inline(SbiIntentJson.Serialize(responses));
    }

    /// <summary>
    /// 处理设备信息查询
    /// </summary>
    private async Task<SbiIntentResult> HandleInfoAsync(CancellationToken cancellationToken)
    {
        var responses = await _sbiService.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        return SbiIntentResult.Inline(SbiIntentJson.Serialize(responses));
    }

    /// <summary>
    /// 处理认证/注册采集
    /// </summary>
    private async Task<SbiIntentResult> HandleCaptureAsync(byte[]? input, bool isRegistration,
        CancellationToken cancellationToken)
    {
        CaptureRequest? request;

        try
        {
            request = SbiIntentJson.Deserialize<CaptureRequest>(input);
        }
        catch (JsonException exception)
        {
            Logger.Warning(exception, "采集请求解析失败");
            return SbiIntentResult.ContentUri(SerializeError(InvalidRequest()));
        }

        if (request is null)
        {
            return SbiIntentResult.ContentUri(SerializeError(InvalidRequest()));
        }

        if (isRegistration)
        {
            if (_registrationService is null)
            {
                return SbiIntentResult.ContentUri(SerializeError(SbiErrorHelper.NoDeviceConnected));
            }

            var registrationResponse = await _registrationService
                .CaptureAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return SbiIntentResult.ContentUri(SbiIntentJson.Serialize(registrationResponse));
        }

        if (_authenticationService is null)
        {
            return SbiIntentResult.ContentUri(SerializeError(SbiErrorHelper.NoDeviceConnected));
        }

        var authenticationResponse = await _authenticationService
            .CaptureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return SbiIntentResult.ContentUri(SbiIntentJson.Serialize(authenticationResponse));
    }

    /// <summary>
    /// 把错误对象序列化为 <see cref="ErrorResponse"/>
    /// </summary>
    private static byte[] SerializeError(SbiError error)
    {
        return SbiIntentJson.Serialize(new ErrorResponse { Error = error });
    }

    /// <summary>
    /// 构造异常路径的返回结果
    /// </summary>
    private static SbiIntentResult CreateErrorResult(SbiError error, bool isContentUri)
    {
        var payload = SerializeError(error);
        return isContentUri ? SbiIntentResult.ContentUri(payload) : SbiIntentResult.Inline(payload);
    }

    /// <summary>
    /// 构造“请求非法”错误；每次返回新实例，避免调用方修改共享对象
    /// </summary>
    private static SbiError InvalidRequest() =>
        SbiErrorHelper.Custom(InternalErrorCode, "Invalid request");
}
