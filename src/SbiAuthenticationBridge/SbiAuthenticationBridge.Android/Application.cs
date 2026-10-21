using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Device;
using SbiAuthenticationBridge.Device.Fcg3M115;
using SbiAuthenticationBridge.Host;
using SbiAuthenticationBridge.Host.Intent;
using Serilog;
using System;
using System.IO;

namespace SbiAuthenticationBridge.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    // 不能在静态字段中缓存：Application 类加载早于 LoggingSetup.EnsureConfigured，
    // 提前取到的 logger 会绑定到 Serilog 的静默默认实例。
    private static ILogger Logger => Log.ForContext("SourceContext", "Android");

    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Android 当前目录不可写；日志统一写到应用私有目录
        var filesDirectory = FilesDir?.AbsolutePath;
        if (!string.IsNullOrEmpty(filesDirectory))
        {
            LoggingSetup.SetLogDirectory(Path.Combine(filesDirectory, "logs"));
        }

        LoggingSetup.EnsureConfigured();
        Logger.Information("Android 宿主启动");

        // 在 Prism 容器构建阶段创建并注册平台服务；此时 Avalonia 已开始初始化
        App.RegisterPlatformServices(registry =>
        {
            ConfigureFtmSdk();

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
    /// 配置 ftmBridgeAPI 与 FCG-3M115 Android SDK
    /// </summary>
    /// <remarks>
    /// APK 中的 INI 位于只读 assets，SDK 需要可读的绝对路径，因此先复制到 FilesDir。
    /// Host 私钥从 Keystore 别名读取；SBI 应用公钥证书与可选 Root 覆盖文件放在 FilesDir。
    /// </remarks>
    private void ConfigureFtmSdk()
    {
        try
        {
            // 即使后续配置失败，也要保留 fd 打开器
            FAP20.UsbHost = new AndroidFap20UsbHost(this);

            var filesDir = FilesDir?.AbsolutePath;
            if (string.IsNullOrEmpty(filesDir))
            {
                throw new InvalidOperationException("无法取得 Android FilesDir。");
            }

            // SBI 请求/响应原始报文保存到应用私有目录，便于对照 CTK 用例
            SbiMessageLog.Directory = Path.Combine(filesDir, "sbi-messages");

            // 设备级原始报文：用于确认固件返回的是 JSON 还是 FCG-3M115 FCP1
            Fcg3M115MessageLog.Directory = Path.Combine(filesDir, "fcg-messages");

            var configPath = Path.Combine(filesDir, "FingerPrintDebug.INI");
            using (var source = Assets?.Open("FingerPrintDebug.INI")
                                ?? throw new FileNotFoundException("APK 中缺少 FingerPrintDebug.INI。"))
            using (var target = File.Create(configPath))
            {
                source.CopyTo(target);
            }

            Fap20AndroidSdk.SetConfigPath(configPath);
            Fap20AndroidSdk.InitLog(Path.Combine(filesDir, "ftmBridgeAPI.log"), 2);

            // 可选公开证书：随包提供时复制到 FilesDir，也可由部署脚本直接放入 FilesDir
            CopyOptionalAsset("fcg3m115_app.cert.der", filesDir);
            CopyOptionalAsset("fcg3m115_root_ca.cert.der", filesDir);

            // Host 私钥优先从 Keystore 读取；开发/联调环境也允许使用 FilesDir 中的 P12
            var hostP12Path = Path.Combine(filesDir, "fcg3m115_host.p12");
            var hostPasswordPath = Path.Combine(filesDir, "fcg3m115_host.password");
            Fcg3M115Configuration.HostKeyFactory =
                () => LoadHostKey(hostP12Path, hostPasswordPath);

            var rootPath = Path.Combine(filesDir, "fcg3m115_root_ca.cert.der");
            Fcg3M115Configuration.RootCertificateFactory = () =>
                File.Exists(rootPath) ? Fcg3M115Configuration.LoadCertificate(rootPath) : null;

            var applicationCertificatePath = Path.Combine(filesDir, "fcg3m115_app.cert.der");
            Fcg3M115Configuration.ApplicationCertificateFactory = () =>
                File.Exists(applicationCertificatePath)
                    ? Fcg3M115Configuration.LoadCertificate(applicationCertificatePath)
                    : null;

            using (var keystoreKey = AndroidKeystoreHostKey.TryCreate(AndroidKeystoreHostKey.Alias))
            {
                if (keystoreKey is not null)
                {
                    Logger.Information("FCG-3M115 Host 密钥来源：Android Keystore");
                }
                else if (File.Exists(hostP12Path))
                {
                    Logger.Information("FCG-3M115 Host 密钥来源：P12 文件 {Path:l}", hostP12Path);
                }
                else
                {
                    Logger.Warning(
                        "未找到 FCG-3M115 Host 私钥：Keystore 别名 {Alias} 不存在，且 {Path:l} 不存在。",
                        AndroidKeystoreHostKey.Alias, hostP12Path);
                }
            }

            if (!File.Exists(applicationCertificatePath))
            {
                Logger.Warning("未找到 SBI 应用证书 {Path}，FCG-3M115 采图将失败。",
                    applicationCertificatePath);
            }

            Logger.Information("ftmBridgeAPI Android SDK 已初始化：{Config:l}", configPath);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "初始化 ftmBridgeAPI Android SDK 失败");
        }
    }

    /// <summary>
    /// 读取 FCG-3M115 Host 私钥：优先 Android Keystore，其次 FilesDir 中的 P12
    /// </summary>
    /// <param name="p12Path">P12 文件路径</param>
    /// <param name="passwordPath">密码文件路径（纯文本，仅开发联调使用）</param>
    private static IFcg3M115HostKey? LoadHostKey(string p12Path, string passwordPath)
    {
        var keystoreKey = AndroidKeystoreHostKey.TryCreate(AndroidKeystoreHostKey.Alias);
        if (keystoreKey is not null)
        {
            return keystoreKey;
        }

        if (!File.Exists(p12Path))
        {
            return null;
        }

        var password = File.Exists(passwordPath)
            ? File.ReadAllText(passwordPath).Trim()
            : null;

        return Pkcs12HostKey.Load(p12Path, password);
    }

    /// <summary>
    /// 把可选的资产复制到应用私有目录；资产不存在时保留已有文件
    /// </summary>
    private void CopyOptionalAsset(string assetName, string destinationDirectory)
    {
        try
        {
            using var source = Assets?.Open(assetName);
            if (source is null)
            {
                return;
            }

            using var target = File.Create(Path.Combine(destinationDirectory, assetName));
            source.CopyTo(target);
        }
        catch (Java.IO.FileNotFoundException)
        {
            // 未随包提供，保留 FilesDir 中由部署流程放入的文件
        }
    }

    /// <summary>
    /// Debug 构建使用模拟 FAP20 便于无硬件测试 Intent；Release 构建使用真实设备。
    /// </summary>
    private static (SbiDeviceEntry Entry, IAuthenticationDevice AuthenticationDevice) CreateDevice()
    {
// #if DEBUG
//         var simulator = new SimulatedFap20Device(SbiIntentActions.DefaultAppId);
//         Logger.Information("使用模拟 FAP20 设备");
//         return (new SbiDeviceEntry("模拟 FAP20", simulator, simulator), simulator);
// #else
//         // FAP20 使用 FingerPrintDebug.INI 中的默认 VID/PID；服务句柄通过 ISbiDevice 访问设备。
//         var fap20 = new FAP20();
//         var authenticationDevice = new Fap20AuthenticationDevice(fap20,
//             SbiIntentActions.DefaultAppId);
//
//         return (new SbiDeviceEntry("FAP20 指纹仪", fap20, authenticationDevice), authenticationDevice);
// #endif

// FAP20 使用 FingerPrintDebug.INI 中的默认 VID / PID；服务句柄通过 ISbiDevice 访问设备。
        var fap20 = new FAP20();
        var authenticationDevice = new Fap20AuthenticationDevice(fap20,
            SbiIntentActions.DefaultAppId);

        return (new SbiDeviceEntry("FAP20 指纹仪", fap20, authenticationDevice), authenticationDevice);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
