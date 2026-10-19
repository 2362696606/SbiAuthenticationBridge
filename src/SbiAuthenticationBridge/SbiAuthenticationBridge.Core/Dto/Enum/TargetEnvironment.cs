using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 设备注册的目标环境，对应 deviceInfo.env
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetEnvironment
{
    /// <summary>
    /// 未注册（报文取值 "None"）
    /// </summary>
    [JsonStringEnumMemberName("None")] None,

    /// <summary>
    /// 预发布环境（报文取值 "Staging"）
    /// </summary>
    [JsonStringEnumMemberName("Staging")] Staging,

    /// <summary>
    /// 开发环境（报文取值 "Developer"）
    /// </summary>
    [JsonStringEnumMemberName("Developer")]
    Developer,

    /// <summary>
    /// 生产前环境（报文取值 "Pre-Production"，带连字符）
    /// </summary>
    [JsonStringEnumMemberName("Pre-Production")]
    PreProduction,

    /// <summary>
    /// 生产环境（报文取值 "Production"）
    /// </summary>
    [JsonStringEnumMemberName("Production")]
    Production
}