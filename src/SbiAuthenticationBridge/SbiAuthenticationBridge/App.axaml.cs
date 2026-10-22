using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Prism.DryIoc;
using Prism.Ioc;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    /// <summary>
    /// 平台注册
    /// </summary>
    public Action<IContainerRegistry>? PlatformRegistrations { get; set; }

    /// <summary>
    /// 窗体工厂方法
    /// </summary>
    public Func<AvaloniaObject>? ShellFactory { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () =>
                MainWindow as Control ?? throw new InvalidOperationException("MainWindow is not set");
        }

        base.OnFrameworkInitializationCompleted();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        PlatformRegistrations?.Invoke(containerRegistry);
    }

    protected override AvaloniaObject CreateShell()
    {
        if (ShellFactory != null)
            return ShellFactory();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            return Container.Resolve<MainWindow>();
        }

        if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            return Container.Resolve<MainView>();
        }

        if (ApplicationLifetime is IActivityApplicationLifetime)
        {
            return Container.Resolve<MainView>();
        }

        throw new NotSupportedException();
    }
}