using Prism.Ioc;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge.Desktop;

public class DesktopAppBuilder
{
    public App Builder()
    {
        var app = new App
        {
            PlatformRegistrations = RegisterTypes
        };
        app.ShellFactory = () => app.Container.Resolve<MainWindow>();
        return app;
    }

    private void RegisterTypes(IContainerRegistry obj)
    {
        
    }
}