using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prism.Container.DryIoc;
using Prism.DryIoc;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Security;
using SbiAuthenticationBridge.Services;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge;

public class App : PrismApplication
{
    /// <summary>
    /// 证书材料配置节
    /// </summary>
    private const string CertificateProviderSection = "CertificateProvider";

    /// <summary>
    /// DKL1 叶证书配置节
    /// </summary>
    private const string Dkl1IssuerSection = "Dkl1Issuer";

    private ConfigurationManager? _configuration;

    private readonly IServiceCollection _serviceCollection = new ServiceCollection();

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
        ConfigureConfiguration();
        ConfigureServices();

        containerRegistry.RegisterInstance<IConfiguration>(_configuration!);
        containerRegistry.GetContainer().Populate(_serviceCollection);

        containerRegistry.RegisterSingleton<IDeviceSelectionService, DeviceSelectionService>();
        containerRegistry.RegisterInstance<ISbiHostContext>(new SbiHostContext("http://127.0.0.1:4501"));

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

    /// <summary>
    /// 允许 DryIoc 从多个公共构造函数中选择参数可解析的那个；
    /// <c>IOptions</c> 相关类型（如 OptionsFactory&lt;&gt;）依赖该规则。
    /// </summary>
    protected override Rules CreateContainerRules() =>
        base.CreateContainerRules().With(FactoryMethod.ConstructorWithResolvableArguments);

    private void ConfigureServices()
    {
        var configuration = _configuration!;

        _serviceCollection.AddOptions();
        _serviceCollection.Configure<CertificateProviderOptions>(
            configuration.GetSection(CertificateProviderSection));
        _serviceCollection.Configure<Dkl1IssuerOptions>(
            configuration.GetSection(Dkl1IssuerSection));
        _serviceCollection.AddSingleton<ICertificateProvider, FileCertificateProvider>();
    }

    private void ConfigureConfiguration()
    {
        _configuration = new ConfigurationManager();
        AddPlatformConfiguration?.Invoke(_configuration);
    }
}