namespace SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

/// <summary>
/// 日志输出等级：<c>ftm_log_init</c> 的 level 参数，低于该等级的日志不输出
/// </summary>
/// <remarks>
/// 取值与 ftmBridgeAPI.h 中的 <c>FTM_LOG_LEVEL_*</c> 一致，顺序即等级由低到高。
/// </remarks>
public enum FtmLogLevel
{
    /// <summary>输出全部日志（Debug / Info / Warn / Error）</summary>
    Debug = 0,

    /// <summary>输出 Info / Warn / Error</summary>
    Info = 1,

    /// <summary>仅输出 Warn / Error</summary>
    Warn = 2,

    /// <summary>仅输出 Error</summary>
    Error = 3,

    /// <summary>关闭所有日志输出</summary>
    None = 4
}
