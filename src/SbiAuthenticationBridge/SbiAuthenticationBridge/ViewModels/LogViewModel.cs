using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Logging;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 日志视图模型：从内存缓冲批量读取日志，提供级别/内容过滤、自动滚动与清除
/// </summary>
public partial class LogViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 单次 UI 刷新最多消费的日志条数，剩余条目留到下一轮
    /// </summary>
    private const int MaxDrainPerPass = 500;

    private readonly ILogEntryStore _store;

    /// <summary>
    /// 等待在 UI 线程消费的日志
    /// </summary>
    private readonly ConcurrentQueue<LogEntryDto> _pending = new();

    /// <summary>
    /// 是否已向 UI 线程投递消费任务，避免高频日志产生大量调度
    /// </summary>
    private int _drainScheduled;

    private bool _disposed;

    /// <summary>
    /// 创建日志视图模型
    /// </summary>
    /// <param name="store">日志内存缓冲</param>
    public LogViewModel(ILogEntryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        LevelOptions =
        [
            new LogLevelOption(LogLevel.Trace, "全部"),
            new LogLevelOption(LogLevel.Debug, "Debug"),
            new LogLevelOption(LogLevel.Information, "Information"),
            new LogLevelOption(LogLevel.Warning, "Warning"),
            new LogLevelOption(LogLevel.Error, "Error"),
            new LogLevelOption(LogLevel.Critical, "Critical")
        ];

        SelectedLevelOption = LevelOptions[0];

        _store.EntryAdded += OnEntryAdded;
        _store.Cleared += OnCleared;
        RebuildEntries();
    }

    /// <summary>
    /// 请求视图滚动到底部
    /// </summary>
    public event EventHandler? ScrollToEndRequested;

    /// <summary>
    /// 可选的级别过滤项
    /// </summary>
    public IReadOnlyList<LogLevelOption> LevelOptions { get; }

    /// <summary>
    /// 过滤后的日志列表
    /// </summary>
    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    /// <summary>
    /// 当前级别过滤项
    /// </summary>
    [ObservableProperty]
    public partial LogLevelOption? SelectedLevelOption { get; set; }

    /// <summary>
    /// 内容过滤关键字
    /// </summary>
    [ObservableProperty]
    public partial string? FilterText { get; set; }

    /// <summary>
    /// 是否自动滚动到最新日志
    /// </summary>
    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    /// <summary>
    /// 当前显示的日志条数
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCountText))]
    [NotifyPropertyChangedFor(nameof(HasEntries))]
    public partial int EntryCount { get; set; }

    /// <summary>
    /// 条数文本
    /// </summary>
    public string EntryCountText => $"{EntryCount} 条";

    /// <summary>
    /// 是否存在日志
    /// </summary>
    public bool HasEntries => EntryCount > 0;

    /// <summary>
    /// 缓冲容量说明
    /// </summary>
    public string BufferDescription => $"最多保留 {_store.Capacity} 条";

    /// <summary>
    /// 清除内存缓冲与当前列表
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        _pending.Clear();
        _store.Clear();
    }

    partial void OnSelectedLevelOptionChanged(LogLevelOption? value)
    {
        _ = value;
        RebuildEntries();
    }

    partial void OnFilterTextChanged(string? value)
    {
        _ = value;
        RebuildEntries();
    }

    partial void OnAutoScrollChanged(bool value)
    {
        if (value)
        {
            RequestScrollToEnd();
        }
    }

    /// <summary>
    /// 缓冲新增日志，可能在任意线程触发
    /// </summary>
    private void OnEntryAdded(object? sender, LogEntryDto entry)
    {
        if (_disposed)
        {
            return;
        }

        _pending.Enqueue(entry);
        ScheduleDrain();
    }

    /// <summary>
    /// 缓冲被清空时同步清空界面
    /// </summary>
    private void OnCleared(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ClearVisibleEntries();
            return;
        }

        Dispatcher.UIThread.Post(ClearVisibleEntries, DispatcherPriority.Background);
    }

    /// <summary>
    /// 保证同一时刻只有一个消费任务在排队
    /// </summary>
    private void ScheduleDrain()
    {
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainPending, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 在 UI 线程批量写入待处理日志
    /// </summary>
    private void DrainPending()
    {
        if (_disposed)
        {
            _pending.Clear();
            Interlocked.Exchange(ref _drainScheduled, 0);
            return;
        }

        var appended = false;
        var drained = 0;

        while (drained < MaxDrainPerPass && _pending.TryDequeue(out var entry))
        {
            drained++;

            if (!Matches(entry))
            {
                continue;
            }

            Entries.Add(new LogEntryViewModel(entry));
            appended = true;
        }

        if (appended)
        {
            TrimEntries();
            RefreshEntryCount();

            if (AutoScroll)
            {
                RequestScrollToEnd();
            }
        }

        Interlocked.Exchange(ref _drainScheduled, 0);

        // 消费期间可能又有新日志入队
        if (!_pending.IsEmpty)
        {
            ScheduleDrain();
        }
    }

    /// <summary>
    /// 按当前过滤条件从缓冲快照重建列表
    /// </summary>
    private void RebuildEntries()
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildEntries, DispatcherPriority.Background);
            return;
        }

        // 先丢弃待处理队列再取快照，避免同一日志重复显示
        _pending.Clear();

        var snapshot = _store.Snapshot();
        Entries.Clear();

        foreach (var entry in snapshot)
        {
            if (Matches(entry))
            {
                Entries.Add(new LogEntryViewModel(entry));
            }
        }

        RefreshEntryCount();

        if (AutoScroll)
        {
            RequestScrollToEnd();
        }
    }

    /// <summary>
    /// 判断日志是否符合当前过滤条件
    /// </summary>
    private bool Matches(LogEntryDto entry)
    {
        var minimumLevel = SelectedLevelOption?.MinimumLevel ?? LogLevel.Trace;

        if (entry.Level < minimumLevel)
        {
            return false;
        }

        var filter = FilterText;

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (entry.ExceptionText?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.SourceContext?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// 控制界面条目数不超过缓冲容量
    /// </summary>
    private void TrimEntries()
    {
        while (Entries.Count > _store.Capacity)
        {
            Entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// 清空界面列表
    /// </summary>
    private void ClearVisibleEntries()
    {
        Entries.Clear();
        RefreshEntryCount();
    }

    private void RefreshEntryCount()
    {
        EntryCount = Entries.Count;
    }

    private void RequestScrollToEnd()
    {
        ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.EntryAdded -= OnEntryAdded;
        _store.Cleared -= OnCleared;
        GC.SuppressFinalize(this);
    }
}
