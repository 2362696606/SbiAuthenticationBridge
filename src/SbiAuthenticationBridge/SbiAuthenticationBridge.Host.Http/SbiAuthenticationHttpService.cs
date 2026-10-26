using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Host.Http.Routing;

namespace SbiAuthenticationBridge.Host.Http;

/// <summary>
/// 认证用途的 HTTP 宿主服务
/// </summary>
internal sealed class SbiAuthenticationHttpService : SbiHttpServiceBase, ISbiAuthenticationHttpService
{
    private static readonly string[] DiscoveryMethods = ["MOSIPDISC", "POST"];
    private static readonly string[] DeviceInfoMethods = ["MOSIPDINFO", "POST"];
    private static readonly string[] CaptureMethods = ["CAPTURE", "POST"];

    /// <summary>
    /// 创建认证用途的 HTTP 宿主服务
    /// </summary>
    /// <param name="applicationServices">组合根服务提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiAuthenticationHttpService(
        IServiceProvider applicationServices,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
        : base(applicationServices, hostContext, loggerFactory)
    {
    }

    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Auth;

    /// <inheritdoc />
    protected override string ServiceName => "认证服务";

    /// <inheritdoc />
    protected override void RegisterServiceDependencies(
        IServiceCollection services,
        IServiceProvider applicationServices)
    {
        services.AddSingleton(_ => applicationServices.GetRequiredService<ISbiAuthenticationServiceHandle>());
    }

    /// <inheritdoc />
    protected override void MapRoutes(WebApplication application)
    {
        application.MapMethods("/device", DiscoveryMethods, DiscoverAsync);
        application.MapMethods("/info", DeviceInfoMethods, GetDeviceInfoAsync);
        application.MapMethods("/capture", CaptureMethods, CaptureAsync);
    }

    /// <summary>
    /// 设备发现
    /// </summary>
    private static async Task<IResult> DiscoverAsync(
        ISbiAuthenticationServiceHandle service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var request = await SbiHttpProtocol
                .ReadJsonAsync<DiscoveryRequest>(context, required: false, cancellationToken)
                .ConfigureAwait(false)
            ?? new DiscoveryRequest();

        var responses = await service.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);
        return SbiHttpProtocol.Json(responses);
    }

    /// <summary>
    /// 获取设备信息
    /// </summary>
    private static async Task<IResult> GetDeviceInfoAsync(
        ISbiAuthenticationServiceHandle service,
        CancellationToken cancellationToken)
    {
        var responses = await service.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        return SbiHttpProtocol.Json(responses);
    }

    /// <summary>
    /// 认证采集
    /// </summary>
    private static async Task<IResult> CaptureAsync(
        ISbiAuthenticationServiceHandle service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var request = await SbiHttpProtocol
                .ReadJsonAsync<CaptureRequest>(context, required: true, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new BadHttpRequestException("请求体不能为空。");

        var response = await service.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
        return SbiHttpProtocol.Json(response);
    }
}
