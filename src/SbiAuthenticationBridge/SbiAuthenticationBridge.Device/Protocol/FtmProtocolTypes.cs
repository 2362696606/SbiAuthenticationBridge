namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// DISCOVER 请求中的设备类型
/// </summary>
public enum FtmDeviceType
{
    /// <summary>指纹设备</summary>
    Finger = 0,

    /// <summary>综合生物识别设备</summary>
    BiometricDevice = 1
}