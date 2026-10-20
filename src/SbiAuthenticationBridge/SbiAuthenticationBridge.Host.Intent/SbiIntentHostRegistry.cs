using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 进程内 Intent 宿主注册表
/// </summary>
/// <remarks>
/// <para>
/// Android 应用在启动时把已创建的服务句柄注册到这里；Activity 每次收到请求时取当前实例。
/// 未注册时使用空宿主，发现返回空列表、采集返回“无设备连接”错误，不会崩溃。
/// </para>
/// <para>
/// 注册不会自动启动服务。主应用需要调用 <see cref="StartAsync"/> 开始处理请求，
/// 调用 <see cref="StopAsync"/> 停止处理并取消进行中的请求。
/// </para>
/// </remarks>
public static class SbiIntentHostRegistry
{
    private static SbiIntentHost _current = new();

    /// <summary>
    /// 当前生效的宿主
    /// </summary>
    public static SbiIntentHost Current => Volatile.Read(ref _current);

    /// <summary>
    /// 注册宿主实例
    /// </summary>
    /// <param name="host">宿主实例</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> 为 null</exception>
    public static void Register(SbiIntentHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Volatile.Write(ref _current, host);
    }

    /// <summary>
    /// 注册服务句柄
    /// </summary>
    /// <param name="sbiService">设备发现/信息查询服务</param>
    /// <param name="authenticationService">认证采集服务</param>
    /// <param name="registrationService">注册采集服务</param>
    /// <param name="appId">应用标识；为空时使用默认值</param>
    public static void Register(ISbiServiceHandle? sbiService,
        IAuthenticationServiceHandle? authenticationService = null,
        IRegisterSbiServiceHandle? registrationService = null,
        string? appId = null)
    {
        Register(new SbiIntentHost(sbiService, authenticationService, registrationService, appId));
    }

    /// <summary>
    /// 启动当前宿主
    /// </summary>
    /// <returns>启动任务</returns>
    public static Task StartAsync() => Current.StartAsync();

    /// <summary>
    /// 停止当前宿主
    /// </summary>
    /// <returns>停止任务</returns>
    public static Task StopAsync() => Current.StopAsync();

    /// <summary>
    /// 恢复为空宿主（主要用于测试）
    /// </summary>
    public static void Reset()
    {
        Volatile.Write(ref _current, new SbiIntentHost());
    }
}
