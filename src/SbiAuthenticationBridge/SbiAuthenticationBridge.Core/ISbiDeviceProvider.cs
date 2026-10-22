namespace SbiAuthenticationBridge.Core;

public interface ISbiDeviceProvider
{
    IReadOnlyCollection<ISbiDevice> Devices { get; }
}