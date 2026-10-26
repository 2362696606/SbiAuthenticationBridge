namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 以 HTTP 方式对外提供 SBI 服务的宿主
/// </summary>
public interface ISbiHttpService : ISbiService
{
    /// <summary>
    /// 监听端口
    /// </summary>
    /// <remarks>
    /// 启动前可赋值，0 表示由操作系统分配；启动后为该实例实际绑定的端口。
    /// 运行期间赋值将抛出异常。
    /// </remarks>
    int Port { get; set; }
}
