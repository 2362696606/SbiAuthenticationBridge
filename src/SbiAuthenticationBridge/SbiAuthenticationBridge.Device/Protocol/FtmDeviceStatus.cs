namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// ftm_setDevStatus 的设备工作状态
/// </summary>
public enum FtmDeviceStatus : byte
{
    /// <summary>正常</summary>
    Normal = 0,

    /// <summary>忙</summary>
    Busy = 1,

    /// <summary>未就绪</summary>
    NotReady = 2,

    /// <summary>就绪</summary>
    Ready = 3,

    /// <summary>未注册</summary>
    NotRegistered = 4
}