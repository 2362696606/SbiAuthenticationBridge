using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// Host 服务注册扩展
/// </summary>
public static class SbiHostServiceCollectionExtensions
{
    /// <summary>
    /// 注册认证与注册两个 ServiceHandle
    /// </summary>
    /// <remarks>
    /// 依赖 <see cref="ISbiDeviceProvider"/>、<see cref="ISbiHostContext"/> 与日志管线，
    /// 由组合根在解析前提供。
    /// </remarks>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，便于链式调用</returns>
    public static IServiceCollection AddSbiHostServices(this IServiceCollection services)
    {
        services.AddSingleton<ISbiAuthenticationServiceHandle, SbiAuthenticationServiceHandle>();
        services.AddSingleton<ISbiRegistrationServiceHandle, SbiRegistrationServiceHandle>();

        return services;
    }
}
