using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Android.Util;
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
        /// <summary>
        /// 配置文件名
        /// </summary>
        private const string AppSettingFileName = "AppSetting.yaml";

        /// <summary>
        /// APK 内置配置的 asset 路径
        /// </summary>
        private const string AppSettingAssetPath = "Config/AppSetting.yaml";

        /// <summary>
        /// 平台日志标签
        /// </summary>
        private const string LogTag = "SbiConfig";

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
            var path = Path.Combine(directory, AppSettingFileName);

            EnsureConfigurationFile(path);
            builder.AddYamlFile(path, optional: true, reloadOnChange: false);
        }

        /// <summary>
        /// 首次启动时把 APK 内置配置复制到应用私有目录
        /// </summary>
        /// <param name="path">目标配置文件路径</param>
        private void EnsureConfigurationFile(string path)
        {
            if (File.Exists(path))
            {
                return;
            }

            try
            {
                using var asset = Assets?.Open(AppSettingAssetPath);
                if (asset is null)
                {
                    return;
                }

                using var target = File.Create(path);
                asset.CopyTo(target);
            }
            catch (Exception exception) when (exception is IOException or Java.IO.IOException)
            {
                Log.Warn(LogTag, $"从 APK 复制默认配置失败：{exception.Message}");
            }
        }
    }
}
