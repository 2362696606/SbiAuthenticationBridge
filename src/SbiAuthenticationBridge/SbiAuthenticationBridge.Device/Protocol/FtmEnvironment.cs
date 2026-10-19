namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// CAPTURE 请求中的采集环境
/// </summary>
public enum FtmEnvironment
{
    /// <summary>Staging</summary>
    Staging = 0,

    /// <summary>Developer</summary>
    Developer = 1,

    /// <summary>Pre-Production</summary>
    PreProduction = 2,

    /// <summary>Production</summary>
    Production = 3
}