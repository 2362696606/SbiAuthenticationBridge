using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Host.Intent;
using AndroidIntent = Android.Content.Intent;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// 平台桥：把组合根容器交给 Intent 宿主，并在冷启动时等待应用初始化
/// </summary>
internal static class SbiIntentBridge
{
    /// <summary>
    /// 冷启动等待应用初始化的上限
    /// </summary>
    private const int InitializationTimeoutMs = 15_000;

    /// <summary>
    /// 应用初始化完成信号；由组合根在 Avalonia 初始化后触发
    /// </summary>
    private static readonly TaskCompletionSource<IServiceProvider> Ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static volatile IServiceProvider? _services;

    private static int _servicesStarted;

    /// <summary>
    /// 保存组合根容器；可重复调用
    /// </summary>
    /// <param name="services">组合根服务提供者</param>
    public static void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        Ready.TrySetResult(services);
    }

    /// <summary>
    /// 获取分发器；冷启动时先拉起主界面完成初始化
    /// </summary>
    public static async Task<ISbiIntentDispatcher> GetDispatcherAsync()
    {
        var services = _services ?? await WaitForInitializationAsync().ConfigureAwait(false);
        await EnsureServicesStartedAsync(services).ConfigureAwait(false);
        return services.GetRequiredService<ISbiIntentDispatcher>();
    }

    private static async Task<IServiceProvider> WaitForInitializationAsync()
    {
        RequestMainActivity();

        var completed = await Task.WhenAny(Ready.Task, Task.Delay(InitializationTimeoutMs))
            .ConfigureAwait(false);
        if (completed != Ready.Task)
        {
            throw new TimeoutException("等待 SBI 应用初始化超时。");
        }

        return await Ready.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 拉起主界面：Avalonia/Prism 容器在其创建时初始化
    /// </summary>
    private static void RequestMainActivity()
    {
        var context = global::Android.App.Application.Context;
        var intent = new AndroidIntent(context, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    /// <summary>
    /// 首次就绪时自动启动宿主服务；用户手动停止后不再自动拉起
    /// </summary>
    private static async Task EnsureServicesStartedAsync(IServiceProvider services)
    {
        if (Interlocked.Exchange(ref _servicesStarted, 1) != 0)
        {
            return;
        }

        await StartServiceAsync(services.GetService<ISbiAuthenticationService>()).ConfigureAwait(false);
    }

    private static async Task StartServiceAsync(ISbiService? service)
    {
        if (service is null || service.IsRunning)
        {
            return;
        }

        try
        {
            await service.StartAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // 并发启动时忽略：已由另一路径启动
        }
    }
}
