using System;
using Avalonia.Controls;
using Avalonia.Threading;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

/// <summary>
/// 日志视图：收到滚动请求时把日志列表滚动到底部
/// </summary>
public partial class LogView : UserControl
{
    private LogViewModel? _viewModel;

    /// <summary>
    /// 创建日志视图
    /// </summary>
    public LogView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
        }

        _viewModel = DataContext as LogViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
        }

        base.OnDataContextChanged(e);
    }

    /// <summary>
    /// 等布局完成后滚动到底部
    /// </summary>
    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>
    /// 把列表滚动到最后一条日志
    /// </summary>
    private void ScrollToEnd()
    {
        if (LogList.ItemCount > 0)
        {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }
}
