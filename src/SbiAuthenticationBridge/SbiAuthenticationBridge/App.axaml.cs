using Avalonia;
using Avalonia.Markup.Xaml;
using Prism.DryIoc;
using Prism.Ioc;
using SbiAuthenticationBridge.Views;

namespace SbiAuthenticationBridge;

public partial class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        // {
        //     desktop.MainWindow = new MainWindow
        //     {
        //         DataContext = new MainViewModel()
        //     };
        // }
        // else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        // {
        //     singleViewFactoryApplicationLifetime.MainViewFactory =
        //         () => new MainView { DataContext = new MainViewModel() };
        // }
        // else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        // {
        //     singleViewPlatform.MainView = new MainView
        //     {
        //         DataContext = new MainViewModel()
        //     };
        // }

        base.OnFrameworkInitializationCompleted();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        
    }

    protected override AvaloniaObject CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }
}