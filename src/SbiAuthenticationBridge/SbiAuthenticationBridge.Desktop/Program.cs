using System;
using Avalonia;
using Avalonia.Threading;

namespace SbiAuthenticationBridge.Desktop;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // UI 线程上未被捕获的异常
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            StartupErrorReporter.Report(eventArgs.Exception, "运行时错误");
            Environment.Exit(1);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            // 覆盖 Prism 的 RegisterTypes、CreateShell 等启动阶段异常
            StartupErrorReporter.Report(exception, "启动失败");
            Environment.ExitCode = 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(new DesktopAppBuilder().Builder)
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}