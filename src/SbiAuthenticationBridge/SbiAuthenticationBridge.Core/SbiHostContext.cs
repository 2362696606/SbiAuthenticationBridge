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

    private string _callbackId;

    /// <summary>
    /// 创建宿主上下文
    /// </summary>
    /// <param name="callbackId">初始回调标识</param>
    /// <exception cref="ArgumentException">为空、过长或含非可打印 ASCII 字符</exception>
    public SbiHostContext(string callbackId)
    {
        _callbackId = Validate(callbackId);
    }

    /// <inheritdoc />
    public string CallbackId
    {
        get => Volatile.Read(ref _callbackId);
        set => Volatile.Write(ref _callbackId, Validate(value));
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
