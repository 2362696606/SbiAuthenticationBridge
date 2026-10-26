namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 宿主无关的注册服务抽象
/// </summary>
/// <remarks>
/// 上层只依赖本接口即可启停注册服务，不必感知具体宿主形态（HTTP、Intent 等）。
/// </remarks>
public interface ISbiRegistrationService : ISbiService
{
}
