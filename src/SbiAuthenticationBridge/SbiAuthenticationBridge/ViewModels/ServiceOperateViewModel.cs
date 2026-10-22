using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 服务操作视图模型；当前设备由全局设备选择决定
/// </summary>
public class ServiceOperateViewModel : DeviceScopedViewModel
{
    /// <summary>
    /// 创建服务操作视图模型
    /// </summary>
    /// <param name="selectionService">全局设备选择服务</param>
    public ServiceOperateViewModel(IDeviceSelectionService selectionService) : base(selectionService)
    {
    }
}