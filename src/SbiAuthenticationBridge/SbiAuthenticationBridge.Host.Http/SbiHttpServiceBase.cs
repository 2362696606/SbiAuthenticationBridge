using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Host.Http.Routing;

namespace SbiAuthenticationBridge.Host.Http;

/// <summary>
/// HTTP 宿主服务公共实现：Kestrel 生命周期、端口与回调标识管理
/// </summary>
internal abstract class SbiHttpServiceBase : ISbiHttpService, IAsyncDisposable
{
    /// <summary>
    /// 合法端口上限
    /// </summary>
    private const int MaxPort = 65535;

    private readonly IServiceProvider _applicationServices;
    private readonly ISbiHostContext _hostContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _portLock = new();

    private WebApplication? _application;
    private int _port;
    private int _disposed;

    /// <summary>
    /// 创建 HTTP 宿主服务
    /// </summary>
    /// <param name="applicationServices">组合根服务提供者，用于解析本用途的业务抽象</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    protected SbiHttpServiceBase(
        IServiceProvider applicationServices,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
    {
        _applicationServices = applicationServices;
        _hostContext = hostContext;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// 本服务对应的设备用途
    /// </summary>
    protected abstract DevicePurpose Purpose { get; }

    /// <summary>
    /// 本服务名称，用于日志
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <inheritdoc />
    public bool IsRunning => Volatile.Read(ref _application) is not null;

    /// <inheritdoc />
    public int Port
    {
        get
        {
            lock (_portLock)
            {
                return _port;
            }
        }
        set
        {
            if (value is < 0 or > MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"端口须在 0..{MaxPort} 之间。");
            }

            lock (_portLock)
            {
                if (IsRunning)
                {
                    throw new InvalidOperationException("服务运行期间不能修改端口。");
                }

                _port = value;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException($"{ServiceName} 已启动。");
            }

            int port;
            lock (_portLock)
            {
                port = _port;
            }

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory,
            });

            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(_loggerFactory);
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
            SbiHttpProtocol.AddCorsPolicy(builder.Services);
            RegisterServiceDependencies(builder.Services, _applicationServices);

            var application = builder.Build();
            try
            {
                application.UseMiddleware<SbiExceptionHandlingMiddleware>();
                application.Use(SbiHttpProtocol.HandlePrivateNetworkPreflightAsync);
                application.UseCors(SbiHttpProtocol.CorsPolicyName);
                MapRoutes(application);

                await application.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            var boundPort = ResolveBoundPort(application);
            lock (_portLock)
            {
                _port = boundPort;
            }

            _hostContext.SetCallbackId(Purpose, $"http://127.0.0.1:{boundPort}");
            Volatile.Write(ref _application, application);

            _logger.LogInformation(
                "{ServiceName} 已启动，监听 http://127.0.0.1:{Port}",
                ServiceName,
                boundPort);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ServiceName} 启动失败", ServiceName);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var application = Interlocked.Exchange(ref _application, null);
            if (application is null)
            {
                return;
            }

            try
            {
                await application.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("{ServiceName} 已停止", ServiceName);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // 同一实例可能被注册为多个服务接口，容器会重复释放，这里保证幂等
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();
        }
    }

    /// <summary>
    /// 注册本通往 WebApplication 容器转发的业务抽象
    /// </summary>
    /// <param name="services">WebApplication 服务集合</param>
    /// <param name="applicationServices">组合根服务提供者</param>
    protected abstract void RegisterServiceDependencies(
        IServiceCollection services,
        IServiceProvider applicationServices);

    /// <summary>
    /// 映射本服务的 HTTP 路由
    /// </summary>
    /// <param name="application">WebApplication 实例</param>
    protected abstract void MapRoutes(WebApplication application);

    /// <summary>
    /// 读取实际绑定的端口
    /// </summary>
    private static int ResolveBoundPort(WebApplication application)
    {
        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;

        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("未能获取 HTTP 监听地址。");

        return new Uri(address).Port;
    }
}
