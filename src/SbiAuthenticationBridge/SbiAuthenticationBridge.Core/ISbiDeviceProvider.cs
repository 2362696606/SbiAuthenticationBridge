namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 设备提供者
/// </summary>
public interface ISbiDeviceProvider
{
    /// <summary>
    /// 设备字典，键只做内部标识，不影响业务流程
    /// </summary>
    IReadOnlyDictionary<string, ISbiDevice> Devices { get; }
}