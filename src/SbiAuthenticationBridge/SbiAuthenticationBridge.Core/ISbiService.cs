namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 可启停的 SBI 服务
/// </summary>
public interface ISbiService
{
    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务；未运行时为空操作
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
