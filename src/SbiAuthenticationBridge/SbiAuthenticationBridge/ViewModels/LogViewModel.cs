using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 运行日志视图模型：定时从 <see cref="LogViewSink"/> 取出日志并展示
/// </summary>
public partial class LogViewModel : ObservableObject
{
    private const int MaxEntries = 1000;

    private readonly DispatcherTimer _timer;

    /// <summary>
    /// 创建日志视图模型并开始轮询
    /// </summary>
    public LogViewModel()
    {
        // 保证平台启动代码未显式初始化时 Serilog 也已就绪
        LoggingSetup.EnsureConfigured();

        Drain();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <summary>
    /// 已展示的日志
    /// </summary>
    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    /// <summary>
    /// 是否自动滚动到底部
    /// </summary>
    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    /// <summary>
    /// 清空已展示和待展示的日志
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        LogViewSink.Instance.ClearPending();
        Entries.Clear();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Drain();
    }

    private void Drain()
    {
        var entries = LogViewSink.Instance.Drain();
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            Entries.Add(new LogEntryViewModel(entry));
        }

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
