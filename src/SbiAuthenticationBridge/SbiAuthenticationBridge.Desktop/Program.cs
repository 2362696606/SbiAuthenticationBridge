using System;
using Avalonia;
using Prism.Ioc;
using SbiAuthenticationBridge;
using SbiAuthenticationBridge.Device;
using SbiAuthenticationBridge.Host;
using Serilog;

namespace SbiAuthenticationBridge.Desktop;

sealed class Program
{
    /// <summary>
    /// 桌面端设备发现响应中的回调地址；HTTP 宿主实现后应与监听地址一致
    /// </summary>
    private const string DeviceCallbackId = "http://127.0.0.1:8080/sbi";

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Desktop");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        LoggingSetup.EnsureConfigured();
        Logger.Information("桌面宿主启动");
        LogFcg3M115Configuration();

        try
        {
            ConfigurePlatformServices();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Logger.Information("桌面宿主退出");
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// 启动时提示 FCG-3M115 采图所需的配置
    /// </summary>
    private static void LogFcg3M115Configuration()
    {
        var p12 = Environment.GetEnvironmentVariable("FCG3M115_HOST_P12");
        var appCertificate = Environment.GetEnvironmentVariable("FCG3M115_APP_CERT");

        if (string.IsNullOrWhiteSpace(p12))
        {
            Logger.Warning("未设置 FCG3M115_HOST_P12，真实 FAP20 无法完成 FCG-3M115 采图。");
        }

        if (string.IsNullOrWhiteSpace(appCertificate))
        {
            Logger.Warning("未设置 FCG3M115_APP_CERT，真实 FAP20 无法加密 bioValue。");
        }
    }

    /// <summary>
    /// 向共享界面注入桌面端设备；SBI 服务待 HTTP 宿主实现后注册
    /// </summary>
    private static void ConfigurePlatformServices()
    {
        App.RegisterPlatformServices(registry =>
        {
            var deviceManager = new SbiDeviceManager([CreateDevice()]);
            registry.RegisterInstance(deviceManager);
        });
    }

    /// <summary>
    /// Debug 构建使用模拟 FAP20 便于无硬件测试；Release 构建使用真实设备。
    /// </summary>
    private static SbiDeviceEntry CreateDevice()
    {
#if DEBUG
        var simulator = new SimulatedFap20Device(DeviceCallbackId);
        Logger.Information("使用模拟 FAP20 设备");
        return new SbiDeviceEntry("模拟 FAP20", simulator, simulator);
#else
        var fap20 = new FAP20();
        var authenticationDevice = new Fap20AuthenticationDevice(fap20, DeviceCallbackId);
        return new SbiDeviceEntry("FAP20 指纹仪", fap20, authenticationDevice);
#endif
    }
}
