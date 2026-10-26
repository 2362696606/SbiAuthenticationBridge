namespace SbiAuthenticationBridge.Core;

public interface ISbiDeviceFactory
{
    /// <summary>
    /// 创建设备
    /// </summary>
    /// <returns>设备对象</returns>
    ISbiDevice CreateDevice();
}