using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Prism.DryIoc;
using SbiAuthenticationBridge.ViewModels;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge;

public class App : PrismApplication
{
    private static Action<IContainerRegistry>? _platformServiceRegistration;

    public override void Initialize()
    {
        // 平台入口未初始化时兜底，保证 Shell 创建前的日志也能进入 LogViewSink
        LoggingSetup.EnsureConfigured();

        AvaloniaXamlLoader.Load(this);

        // Prism 的初始化入口，缺少该调用时 CreateShell 不会执行
        base.Initialize();
#if DEBUG
        // Android 上没有可连接的 Avalonia DevTools 实例，连接失败会在 UI 线程
        // 抛出 DevToolsUnreachableException（JavaProxyThrowable）并导致进程崩溃。
        // DevTools 只在桌面调试时启用。
        if (!OperatingSystem.IsAndroid())
        {
            this.AttachDeveloperTools();
        }
#endif
    }

    /// <summary>
    /// 平台入口在 Avalonia 初始化前注册平台专属服务
    /// </summary>
    /// <param name="registration">
    /// 使用 Prism 容器注册平台实例，例如 <c>SbiDeviceManager</c>、<c>ISbiService</c>
    /// </param>
    /// <remarks>
    /// Android 在 <c>Application.OnCreate</c> 调用 <c>base.OnCreate()</c> 之前调用本方法；
    /// Desktop 在 <c>StartWithClassicDesktopLifetime</c> 之前调用。
    /// </remarks>
    public static void RegisterPlatformServices(Action<IContainerRegistry> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _platformServiceRegistration = registration;
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // 平台无关的视图模型与共享服务
        containerRegistry.RegisterSingleton<MainViewModel>();
        containerRegistry.RegisterSingleton<DevicesViewModel>();
        containerRegistry.RegisterSingleton<DeviceViewModel>();
        containerRegistry.RegisterSingleton<SbiServiceViewModel>();
        containerRegistry.RegisterSingleton<LogViewModel>();

        // 平台专属设备/服务（Android、Desktop 在初始化前注册）
        var platformRegistration = _platformServiceRegistration;
        _platformServiceRegistration = null;
        platformRegistration?.Invoke(containerRegistry);
    }

    /// <summary>
    /// 桌面使用 MainWindow 作为 Shell；Android/iOS/浏览器等单视图平台使用 MainView
    /// </summary>
    protected override AvaloniaObject CreateShell()
    {
        var viewModel = Container.Resolve<MainViewModel>();

        // Android 上 ApplicationLifetime 在某些版本/启动时序下可能尚未赋值，
        // 显式按平台选择单视图 Shell，避免把 Window 当成 MainView 导致空白。
        if (OperatingSystem.IsAndroid() || ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            return new MainView { DataContext = viewModel };
        }

        var window = Container.Resolve<MainWindow>();
        window.DataContext = viewModel;
        return window;
    }
}
