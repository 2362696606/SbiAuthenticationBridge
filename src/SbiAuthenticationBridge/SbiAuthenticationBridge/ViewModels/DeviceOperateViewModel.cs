using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 单设备操作视图模型；当前设备由全局设备选择决定
/// </summary>
public class DeviceOperateViewModel : DeviceScopedViewModel
{
    /// <summary>
    /// 创建设备操作视图模型
    /// </summary>
    /// <param name="selectionService">全局设备选择服务</param>
    public DeviceOperateViewModel(IDeviceSelectionService selectionService) : base(selectionService)
    {
    }
}