using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Device;
using SbiAuthenticationBridge.Host;
using SbiAuthenticationBridge.Host.Intent;
using Serilog;

namespace SbiAuthenticationBridge.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Android");

    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        LoggingSetup.EnsureConfigured();
        Logger.Information("Android 宿主启动");

        // 在 Prism 容器构建阶段创建并注册平台服务；此时 Avalonia 已开始初始化
        App.RegisterPlatformServices(registry =>
        {
            var (entry, authenticationDevice) = CreateDevice();

            var deviceManager = new SbiDeviceManager([entry]);
            var authenticationService = new AuthenticationServiceHandle([authenticationDevice]);
            var host = new SbiIntentHost(authenticationService, authenticationService,
                appId: SbiIntentActions.DefaultAppId);

            registry.RegisterInstance(deviceManager);
            registry.RegisterInstance<ISbiService>(host);

            SbiIntentHostRegistry.Register(host);

            // 主应用可通过 SbiIntentHostRegistry.StartAsync/StopAsync 控制 Intent 服务
            _ = host.StartAsync();
            Logger.Information("Android Intent 服务已注册并启动");
        });

        base.OnCreate();
    }

    /// <summary>
    /// Debug 构建使用模拟 FAP20 便于无硬件测试 Intent；Release 构建使用真实设备。
    /// </summary>
    private static (SbiDeviceEntry Entry, IAuthenticationDevice AuthenticationDevice) CreateDevice()
    {
#if DEBUG
        var simulator = new SimulatedFap20Device(SbiIntentActions.DefaultAppId);
        Logger.Information("使用模拟 FAP20 设备");
        return (new SbiDeviceEntry("模拟 FAP20", simulator, simulator), simulator);
#else
        // FAP20 使用 FingerPrintDebug.INI 中的默认 VID/PID；服务句柄通过 ISbiDevice 访问设备。
        var fap20 = new FAP20();
        var authenticationDevice = new Fap20AuthenticationDevice(fap20,
            SbiIntentActions.DefaultAppId);

        return (new SbiDeviceEntry("FAP20 指纹仪", fap20, authenticationDevice), authenticationDevice);
#endif
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
