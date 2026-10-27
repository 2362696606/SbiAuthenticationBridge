using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Device.Android;

/// <summary>
/// Android 设备服务注册扩展
/// </summary>
public static class SbiAndroidDeviceServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Android 平台的 FAP20 设备工厂与提供者
    /// </summary>
    /// <remarks>
    /// 读取 <c>Device:vendorId</c> / <c>Device:productId</c>；未配置时提供者返回空的设备表，
    /// 不伪造设备。依赖 <see cref="IConfiguration"/>、<see cref="ISbiHostContext"/> 与日志管线。
    /// </remarks>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，便于链式调用</returns>
    public static IServiceCollection AddSbiAndroidDevice(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISbiDeviceFactory, AndroidFap20DeviceFactory>();

        services.AddSingleton<ISbiDeviceProvider>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            if (!AndroidFap20Options.TryFromConfiguration(configuration, out _))
            {
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Fap20Android")
                    .LogWarning(
                        "未配置 Device:vendorId / Device:productId，Android 端不创建设备。");

                return new AndroidFap20DeviceProvider(null);
            }

            return new AndroidFap20DeviceProvider(
                provider.GetRequiredService<ISbiDeviceFactory>());
        });

        return services;
    }
}
