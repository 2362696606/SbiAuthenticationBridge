using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主服务注册扩展
/// </summary>
public static class SbiIntentServiceCollectionExtensions
{
    /// <summary>
    /// 注册认证 Intent 宿主服务
    /// </summary>
    /// <remarks>
    /// Android CTK 不支持注册采集，因此不注册注册用途的宿主服务。
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

        return services;
    }
}
