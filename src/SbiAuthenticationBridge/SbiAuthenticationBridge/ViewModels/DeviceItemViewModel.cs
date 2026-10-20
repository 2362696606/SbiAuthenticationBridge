using CommunityToolkit.Mvvm.ComponentModel;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Host;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备列表中的一行
/// </summary>
public sealed class DeviceItemViewModel : ObservableObject
{
    /// <summary>
    /// 创建行视图模型
    /// </summary>
    /// <param name="entry">设备条目</param>
    public DeviceItemViewModel(SbiDeviceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    /// <summary>
    /// 对应的设备条目
    /// </summary>
    public SbiDeviceEntry Entry { get; }

    /// <summary>
    /// 设备名称
    /// </summary>
    public string Name => Entry.Name;

    /// <summary>
    /// 设备角色（认证 / 注册 / 仅 SBI）
    /// </summary>
    public string RoleText => Entry.SbiDevice switch
    {
        IAuthenticationDevice => "认证设备",
        IRegisterSbiDevice => "注册设备",
        null => "设备",
        _ => "SBI 设备"
    };

    /// <summary>
    /// 当前是否已连接
    /// </summary>
    public bool IsConnected => Entry.Device.IsConnected;

    /// <summary>
    /// 连接状态文本
    /// </summary>
    public string ConnectionText => IsConnected ? "已连接" : "未连接";

    /// <summary>
    /// 刷新连接状态显示
    /// </summary>
    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionText));
    }
}
