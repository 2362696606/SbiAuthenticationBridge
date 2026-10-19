namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// 原生命令返回非零结果码时抛出的异常
/// </summary>
/// <remarks>继承 <see cref="InvalidOperationException"/>，保留结果码供上层映射为 SBI 错误。</remarks>
public sealed class FtmCommandException : InvalidOperationException
{
    /// <summary>
    /// 创建原生命令失败异常
    /// </summary>
    /// <param name="operation">原生命令名</param>
    /// <param name="resultCode">设备返回的结果码</param>
    /// <param name="message">错误说明</param>
    public FtmCommandException(string operation, int resultCode, string message)
        : base(message)
    {
        Operation = operation;
        ResultCode = resultCode;
    }

    /// <summary>
    /// 原生命令名
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// 设备返回的结果码
    /// </summary>
    public int ResultCode { get; }
}
