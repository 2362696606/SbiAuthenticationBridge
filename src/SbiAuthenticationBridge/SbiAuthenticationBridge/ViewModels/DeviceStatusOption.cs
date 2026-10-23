using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备状态选项
/// </summary>
/// <param name="Value">SBI 状态值</param>
/// <param name="DisplayName">显示文本</param>
public sealed record DeviceStatusOption(DeviceStatus Value, string DisplayName)
{
    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
