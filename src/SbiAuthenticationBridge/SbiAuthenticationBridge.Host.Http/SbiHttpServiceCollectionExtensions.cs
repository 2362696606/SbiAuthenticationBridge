using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host.Http;

/// <summary>
/// HTTP 宿主服务注册扩展
/// </summary>
public static class SbiHttpServiceCollectionExtensions
{
    /// <summary>
    /// 注册认证与注册两个 HTTP 宿主服务
    /// </summary>
    /// <remarks>
    /// 每个实现同时注册为 HTTP 接口和宿主无关的用途接口，两者解析到同一实例。
    /// 不直接注册 <see cref="ISbiService"/>，避免两个用途注入歧义。
    /// </remarks>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，便于链式调用</returns>
    public static IServiceCollection AddSbiHttpServices(this IServiceCollection services)
    {
        services.AddSingleton<ISbiAuthenticationHttpService, SbiAuthenticationHttpService>();
        services.AddSingleton<ISbiAuthenticationService>(
            provider => provider.GetRequiredService<ISbiAuthenticationHttpService>());

        services.AddSingleton<ISbiRegistrationHttpService, SbiRegistrationHttpService>();
        services.AddSingleton<ISbiRegistrationService>(
            provider => provider.GetRequiredService<ISbiRegistrationHttpService>());

        return services;
    }
}
