namespace SbiAuthenticationBridge.Core;

public interface ISbiService
{
    /// <summary>
    /// 启动服务
    /// </summary>
    Task StartAsync();
    
    /// <summary>
    /// 停止服务
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 服务是否正在运行
    /// </summary>
    bool IsRunning { get; }
}