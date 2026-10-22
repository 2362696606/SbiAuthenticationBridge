using Microsoft.Extensions.Configuration;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge.Desktop;

public class DesktopAppBuilder
{
    public App Builder()
    {
        var app = new App
        {
            PlatformRegistrations = RegisterTypes,
            AddPlatformConfiguration = ConfigConfiguration
        };
        app.ShellFactory = () => app.Container.Resolve<MainWindow>();
        return app;
    }

    private static void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<ISbiDeviceProvider, FAP20DeviceProvider>();
    }

    private static void ConfigConfiguration(IConfigurationBuilder builder)
    {
        builder.AddIniFile("./FingerPrintDebug.INI", optional: false, reloadOnChange: false);
    }
}