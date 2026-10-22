namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// FTM 原生命令返回非零结果码时抛出的异常
/// </summary>
/// <remarks>
/// 继承 <see cref="InvalidOperationException"/>，保持与原有调用方的兼容；
/// SBI 适配层可通过 <see cref="ResultCode"/> 区分设备忙（0x75）等状态。
/// </remarks>
public sealed class FtmCommandException : InvalidOperationException
{
    /// <summary>
    /// 创建原生命令异常
    /// </summary>
    /// <param name="operation">命令名</param>
    /// <param name="resultCode">原生接口返回码</param>
    /// <param name="message">异常描述</param>
    public FtmCommandException(string operation, int resultCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
        ResultCode = resultCode;
    }

    /// <summary>
    /// 命令名，例如 <c>ftm_capture_auth</c>
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// 原生接口返回码
    /// </summary>
    public int ResultCode { get; }
}
