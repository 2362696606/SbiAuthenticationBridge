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
/// 注册用途的 HTTP 宿主服务
/// </summary>
internal sealed class SbiRegistrationHttpService : SbiHttpServiceBase, ISbiRegistrationHttpService
{
    private static readonly string[] DiscoveryMethods = ["MOSIPDISC", "POST"];
    private static readonly string[] DeviceInfoMethods = ["MOSIPDINFO", "POST"];
    private static readonly string[] CaptureMethods = ["RCAPTURE", "POST"];
    private static readonly string[] StreamMethods = ["STREAM", "POST"];

    /// <summary>
    /// 创建注册用途的 HTTP 宿主服务
    /// </summary>
    /// <param name="applicationServices">组合根服务提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiRegistrationHttpService(
        IServiceProvider applicationServices,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
        : base(applicationServices, hostContext, loggerFactory)
    {
    }

    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Registration;

    /// <inheritdoc />
    protected override string ServiceName => "注册服务";

    /// <inheritdoc />
    protected override void RegisterServiceDependencies(
        IServiceCollection services,
        IServiceProvider applicationServices)
    {
        services.AddSingleton(_ => applicationServices.GetRequiredService<ISbiRegistrationServiceHandle>());
    }

    /// <inheritdoc />
    protected override void MapRoutes(WebApplication application)
    {
        application.MapMethods("/device", DiscoveryMethods, DiscoverAsync);
        application.MapMethods("/info", DeviceInfoMethods, GetDeviceInfoAsync);
        application.MapMethods("/rcapture", CaptureMethods, CaptureAsync);
        application.MapMethods("/stream", StreamMethods, StreamAsync);
    }

    /// <summary>
    /// 设备发现
    /// </summary>
    private static async Task<IResult> DiscoverAsync(
        ISbiRegistrationServiceHandle service,
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
        ISbiRegistrationServiceHandle service,
        CancellationToken cancellationToken)
    {
        var responses = await service.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        return SbiHttpProtocol.Json(responses);
    }

    /// <summary>
    /// 注册采集
    /// </summary>
    private static async Task<IResult> CaptureAsync(
        ISbiRegistrationServiceHandle service,
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

    /// <summary>
    /// 打开注册设备预览流
    /// </summary>
    private static async Task<IResult> StreamAsync(
        ISbiRegistrationServiceHandle service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var request = await SbiHttpProtocol
                .ReadJsonAsync<StreamRequest>(context, required: false, cancellationToken)
                .ConfigureAwait(false)
            ?? new StreamRequest();

        var stream = await service.StreamAsync(request, cancellationToken).ConfigureAwait(false);

        context.Response.ContentType = SbiHttpProtocol.MjpegContentType;
        await using (stream)
        {
            await stream.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
        }

        return Results.Empty;
    }
}
