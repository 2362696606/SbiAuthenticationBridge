using Avalonia;
using Avalonia.Controls;
using Prism.Ioc;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

public partial class DevicesView : UserControl
{
    private bool _refreshed;

    public DevicesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshOnce();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Design.IsDesignMode)
        {
            return;
        }

        DataContext ??= ContainerLocator.Container.Resolve<DevicesViewModel>();
        RefreshOnce();
    }

    /// <summary>
    /// 首次绑定数据上下文时刷新一次设备列表
    /// </summary>
    private void RefreshOnce()
    {
        if (_refreshed || DataContext is not DevicesViewModel viewModel)
        {
            return;
        }

        _refreshed = true;
        _ = viewModel.RefreshCommand.ExecuteAsync(null);
    }
}