using Avalonia;
using Avalonia.Controls;
using Prism.Ioc;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

/// <summary>
/// 服务操作视图：显示认证与注册服务的状态并提供启停控制
/// </summary>
public partial class ServiceOperateView : UserControl
{
    private bool _refreshed;

    /// <summary>
    /// 创建服务操作视图
    /// </summary>
    public ServiceOperateView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshOnce();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Design.IsDesignMode)
        {
            return;
        }

        DataContext ??= ContainerLocator.Container.Resolve<ServiceOperateViewModel>();
        RefreshOnce();
    }

    /// <summary>
    /// 首次绑定数据上下文时同步一次服务状态
    /// </summary>
    private void RefreshOnce()
    {
        if (_refreshed || DataContext is not ServiceOperateViewModel viewModel)
        {
            return;
        }

        _refreshed = true;
        viewModel.Refresh();
    }
}
