using Avalonia;
using Avalonia.Controls;
using Prism.Ioc;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

public partial class DeviceOperateView : UserControl
{
    private bool _refreshed;

    public DeviceOperateView()
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

        DataContext ??= ContainerLocator.Container.Resolve<DeviceOperateViewModel>();
        RefreshOnce();
    }

    /// <summary>
    /// 首次绑定数据上下文时刷新一次设备状态
    /// </summary>
    private void RefreshOnce()
    {
        if (_refreshed || DataContext is not DeviceOperateViewModel viewModel)
        {
            return;
        }

        _refreshed = true;
        _ = viewModel.RefreshStateCommand.ExecuteAsync(null);
    }
}