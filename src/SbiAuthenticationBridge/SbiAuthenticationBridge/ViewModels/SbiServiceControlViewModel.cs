using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 单个 SBI 服务的控制视图模型：状态、端口与启停
/// </summary>
public partial class SbiServiceControlViewModel : ViewModelBase
{
    /// <summary>
    /// 端口范围下限
    /// </summary>
    private const int MinPort = 4501;

    /// <summary>
    /// 端口范围上限
    /// </summary>
    private const int MaxPort = 4600;

    private readonly ISbiService? _service;
    private readonly ISbiHttpService? _httpService;
    private readonly ILogger _logger;

    /// <summary>
    /// 创建服务控制视图模型
    /// </summary>
    /// <param name="displayName">服务显示名称</param>
    /// <param name="service">宿主无关的服务实例；当前平台未提供时为 <see langword="null"/></param>
    /// <param name="defaultPort">HTTP 宿主使用的默认端口</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiServiceControlViewModel(
        string displayName,
        ISbiService? service,
        int defaultPort,
        ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        DisplayName = displayName;
        _service = service;
        _httpService = service as ISbiHttpService;
        _logger = loggerFactory.CreateLogger("SbiServiceControlViewModel");

        IsAvailable = service is not null;
        HasPort = _httpService is not null;
        IsRunning = service?.IsRunning ?? false;

        if (_httpService is null)
        {
            Port = defaultPort;
            return;
        }

        var port = _httpService.Port;
        if (port is < MinPort or > MaxPort)
        {
            port = defaultPort;
            if (!IsRunning)
            {
                _httpService.Port = port;
            }
        }

        Port = port;
    }

    /// <summary>
    /// 服务显示名称
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 当前平台是否提供该服务
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// 是否为 HTTP 宿主（可设置端口）
    /// </summary>
    public bool HasPort { get; }

    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanEditPort))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// 是否正在启动或停止
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanEditPort))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// 监听的端口；非 HTTP 宿主时不显示
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsPortValid))]
    [NotifyPropertyChangedFor(nameof(PortValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasPortValidationError))]
    public partial int Port { get; set; }

    /// <summary>
    /// 最近一次操作错误
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否有错误信息
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// 端口是否在允许范围内
    /// </summary>
    public bool IsPortValid => !HasPort || Port is >= MinPort and <= MaxPort;

    /// <summary>
    /// 端口校验提示
    /// </summary>
    public string? PortValidationMessage =>
        IsPortValid ? null : $"端口须在 {MinPort}–{MaxPort} 之间";

    /// <summary>
    /// 是否显示端口校验错误
    /// </summary>
    public bool HasPortValidationError => HasPort && !IsPortValid;

    /// <summary>
    /// 运行期间是否允许修改端口
    /// </summary>
    public bool CanEditPort => HasPort && !IsRunning && !IsBusy;

    /// <summary>
    /// 状态文本
    /// </summary>
    public string StatusText => !IsAvailable
        ? "当前平台未提供"
        : IsBusy
            ? IsRunning ? "停止中…" : "启动中…"
            : IsRunning
                ? HasPort ? $"运行中（端口 {Port}）" : "运行中"
                : "已停止";

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

        if (!IsPortValid)
        {
            ErrorMessage = PortValidationMessage;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            if (_httpService is not null)
            {
                _httpService.Port = Port;
            }

            await _service.StartAsync();

            if (_httpService is not null && _httpService.Port is >= MinPort and <= MaxPort)
            {
                Port = _httpService.Port;
            }

            if (HasPort)
            {
                _logger.LogInformation("{Service} 已启动，端口={Port}", DisplayName, Port);
            }
            else
            {
                _logger.LogInformation("{Service} 已启动", DisplayName);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogError(exception, "{Service} 启动失败", DisplayName);
        }
        finally
        {
            IsRunning = _service.IsRunning;
            IsBusy = false;
            NotifyCommandStates();
        }
    }

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

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _service.StopAsync();
            _logger.LogInformation("{Service} 已停止", DisplayName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogError(exception, "{Service} 停止失败", DisplayName);
        }
        finally
        {
            IsRunning = _service.IsRunning;
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>
    /// 从服务读取当前运行状态与端口
    /// </summary>
    public void Refresh()
    {
        if (_service is null)
        {
            return;
        }

        IsRunning = _service.IsRunning;

        if (_httpService is not null && _httpService.Port is >= MinPort and <= MaxPort)
        {
            Port = _httpService.Port;
        }
    }

    private bool CanStart() => IsAvailable && !IsRunning && !IsBusy && IsPortValid;

    private bool CanStop() => IsAvailable && IsRunning && !IsBusy;

    partial void OnIsRunningChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnPortChanged(int value)
    {
        if (_httpService is not null && !IsRunning && !IsBusy && IsPortValid)
        {
            _httpService.Port = value;
        }

        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
