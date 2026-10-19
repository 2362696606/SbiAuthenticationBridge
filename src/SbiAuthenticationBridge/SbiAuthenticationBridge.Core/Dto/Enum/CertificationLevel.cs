using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 设备认证等级，对应 deviceInfo.certification
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CertificationLevel
{
    /// <summary>
    /// L0：由主机侧的设备驱动或 MDS 执行加密，信任基于软件，
    /// 不依赖安全启动或 FTM 硬件信任
    /// </summary>
    [JsonStringEnumMemberName("L0")] L0,

    /// <summary>
    /// L1：在设备可信区内执行加密，信任基于带安全执行环境的安全芯片与已认证 FTM
    /// </summary>
    [JsonStringEnumMemberName("L1")] L1
}