using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Prism.DryIoc;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Services;
using SbiAuthenticationBridge.ViewModels;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge;

public class App : PrismApplication
{
    private IConfiguration? _configuration;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
        base.Initialize();
    }

    /// <summary>
    /// 平台配置
    /// </summary>
    public Action<IConfigurationBuilder>? AddPlatformConfiguration { get; set; }

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
        #region 系统配置

        var configurationBuilder = new ConfigurationBuilder();
        AddPlatformConfiguration?.Invoke(configurationBuilder);
        _configuration = configurationBuilder.Build();
        containerRegistry.RegisterInstance(_configuration);

        #endregion

        containerRegistry.RegisterSingleton<IDeviceSelectionService, DeviceSelectionService>();
        containerRegistry.RegisterInstance<ISbiHostContext>(new SbiHostContext("http://127.0.0.1:4501/"));
        // containerRegistry.RegisterSingleton<DevicesViewModel>();

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