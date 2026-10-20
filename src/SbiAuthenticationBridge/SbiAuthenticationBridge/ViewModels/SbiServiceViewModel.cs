using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SbiAuthenticationBridge.Core;
using Serilog;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// SBI 服务控制：启动/停止当前平台提供的服务
/// </summary>
public partial class SbiServiceViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "UI");

    private readonly ISbiService? _service;

    /// <summary>
    /// 创建服务控制视图模型并立即读取一次状态
    /// </summary>
    /// <param name="services">平台注册的 SBI 服务；当前平台未提供时为空集合</param>
    public SbiServiceViewModel(IEnumerable<ISbiService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _service = services.FirstOrDefault();
        Refresh();
    }

    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// 操作失败时的提示
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 当前平台是否注册了 SBI 服务
    /// </summary>
    public bool IsServiceAvailable => _service is not null;

    /// <summary>
    /// 服务状态文本
    /// </summary>
    public string StateText => !IsServiceAvailable
        ? "服务未注册（当前平台未提供 ISbiService）"
        : IsRunning
            ? "运行中"
            : "已停止";

    /// <summary>
    /// 重新读取当前平台服务及其运行状态
    /// </summary>
    public void Refresh()
    {
        ErrorMessage = null;
        IsRunning = _service is ISbiServiceState { IsRunning: true };
        Logger.Debug("SBI 服务状态：{State:l}", StateText);

        OnPropertyChanged(nameof(IsServiceAvailable));
        OnPropertyChanged(nameof(StateText));

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 启动服务
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_service is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("启动 SBI 服务");

        try
        {
            await _service.StartAsync();
            IsRunning = _service is not ISbiServiceState state || state.IsRunning;
            Logger.Information("SBI 服务已启动");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "启动 SBI 服务失败");
            ErrorMessage = exception.Message;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    private bool CanStart() => _service is not null && !IsRunning;

    /// <summary>
    /// 停止服务
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_service is null)
        {
            return;
        }

        ErrorMessage = null;
        Logger.Information("停止 SBI 服务");

        try
        {
            await _service.StopAsync();
            IsRunning = _service is ISbiServiceState { IsRunning: true };
            Logger.Information("SBI 服务已停止");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "停止 SBI 服务失败");
            ErrorMessage = exception.Message;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    private bool CanStop() => _service is not null && IsRunning;

    /// <summary>
    /// 刷新命令
    /// </summary>
    [RelayCommand]
    private void RefreshService() => Refresh();

    partial void OnIsRunningChanged(bool value)
    {
        _ = value;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(StateText));

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
