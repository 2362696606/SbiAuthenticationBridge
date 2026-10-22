namespace SbiAuthenticationBridge.Core;

public interface IDeviceProxy
{
    /// <summary>
    /// 获取设备
    /// </summary>
    IDevice InnerDevice { get; }
}