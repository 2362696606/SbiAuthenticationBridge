using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SbiAuthenticationBridge.Device.Android;
using SbiAuthenticationBridge.Host.Intent;

namespace SbiAuthenticationBridge.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        private const string AppSettingFileName = "AppSetting.yaml";

        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        /// <summary>
        /// Android 没有桌面端的 ./Config 目录，改为从应用私有目录读取配置
        /// </summary>
        protected override AppBuilder CreateAppBuilder()
        {
            return AppBuilder
                .Configure(() => new App
                {
                    AddPlatformConfiguration = ConfigureConfiguration,
                    PlatformServiceRegistrations = ConfigureServices,
                    PlatformReady = SbiIntentBridge.Initialize
                })
                .UseAndroid();
        }

        /// <summary>
        /// 注册 Android 设备与 Intent 宿主服务
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSbiAndroidDevice();
            services.AddSbiIntentServices(SbiIntentAppId.Value);
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }

        private void ConfigureConfiguration(IConfigurationBuilder builder)
        {
            var directory = FilesDir?.AbsolutePath ?? AppContext.BaseDirectory;

            builder.AddYamlFile(Path.Combine(directory, AppSettingFileName),
                optional: true, reloadOnChange: false);
        }
    }
}
