namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 可查询运行状态的 SBI 服务
/// </summary>
/// <remarks>
/// <see cref="ISbiService"/> 只定义启停动作。需要把当前状态反馈到界面的实现可同时实现本接口；
/// 未实现本接口时，调用方只能根据启停命令的结果推断状态。
/// </remarks>
public interface ISbiServiceState
{
    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    bool IsRunning { get; }
}
