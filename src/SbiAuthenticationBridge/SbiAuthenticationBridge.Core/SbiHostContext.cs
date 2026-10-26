using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// <see cref="ISbiHostContext"/> 的线程安全默认实现
/// </summary>
public sealed class SbiHostContext : ISbiHostContext
{
    /// <summary>
    /// 回调标识的最大长度，对应 DISCOVER/DEVICE_INFO 协议限制
    /// </summary>
    private const int MaxCallbackIdLength = 192;

    private readonly string[] _callbackIds =
    [
        string.Empty,
        string.Empty,
    ];

    /// <summary>
    /// 创建宿主上下文
    /// </summary>
    /// <param name="callbackId">认证与注册服务共用的初始回调标识</param>
    /// <exception cref="ArgumentException">为空、过长或含非可打印 ASCII 字符</exception>
    public SbiHostContext(string callbackId)
    {
        var value = Validate(callbackId);
        _callbackIds[(int)DevicePurpose.Auth] = value;
        _callbackIds[(int)DevicePurpose.Registration] = value;
    }

    /// <inheritdoc />
    public string GetCallbackId(DevicePurpose purpose)
    {
        EnsurePurpose(purpose);
        return Volatile.Read(ref _callbackIds[(int)purpose]);
    }

    /// <inheritdoc />
    public void SetCallbackId(DevicePurpose purpose, string callbackId)
    {
        EnsurePurpose(purpose);
        Volatile.Write(ref _callbackIds[(int)purpose], Validate(callbackId));
    }

    private static void EnsurePurpose(DevicePurpose purpose)
    {
        if (purpose is not (DevicePurpose.Auth or DevicePurpose.Registration))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "不支持的设备用途。");
        }
    }

    private static string Validate(string callbackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackId);

        if (callbackId.Length > MaxCallbackIdLength
            || callbackId.Any(static c => c is < ' ' or > '~'))
        {
            throw new ArgumentException(
                $"回调标识须为 1..{MaxCallbackIdLength} 个可打印 ASCII 字符。",
                nameof(callbackId));
        }

        return callbackId;
    }
}
