using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主服务注册扩展
/// </summary>
public static class SbiIntentServiceCollectionExtensions
{
    /// <summary>
    /// 注册认证与注册两个 Intent 宿主服务
    /// </summary>
    /// <remarks>
    /// 每个实现同时注册为宿主无关的用途接口，两者解析到同一实例；
    /// 不直接注册 <see cref="ISbiService"/>，避免两个用途注入歧义。
    /// </remarks>
    /// <param name="services">服务集合</param>
    /// <param name="appId">Android appId（包名），同时作为 callbackId</param>
    /// <returns>原服务集合，便于链式调用</returns>
    public static IServiceCollection AddSbiIntentServices(this IServiceCollection services, string appId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        services.AddSingleton(new SbiIntentHostOptions { AppId = appId });
        services.AddSingleton<ISbiIntentDispatcher, SbiIntentDispatcher>();

        services.AddSingleton<SbiAuthenticationIntentService>();
        services.AddSingleton<ISbiAuthenticationService>(
            provider => provider.GetRequiredService<SbiAuthenticationIntentService>());

        services.AddSingleton<SbiRegistrationIntentService>();
        services.AddSingleton<ISbiRegistrationService>(
            provider => provider.GetRequiredService<SbiRegistrationIntentService>());

        return services;
    }
}
