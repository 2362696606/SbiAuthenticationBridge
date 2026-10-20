using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _viewModel;

    public LogView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged -= OnEntriesChanged;
        }

        _viewModel = DataContext as LogViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.AutoScroll != true)
        {
            return;
        }

        // 等布局完成后再滚动，确保新行已经计算进 Extent
        Dispatcher.UIThread.Post(() => LogScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
    }
}
