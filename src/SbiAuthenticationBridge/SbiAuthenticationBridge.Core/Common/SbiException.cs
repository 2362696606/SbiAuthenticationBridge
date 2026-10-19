using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core.Common;

/// <summary>
/// 携带 SBI 错误对象的异常
/// </summary>
/// <remarks>
/// <para>
/// 用于返回值无法表达错误的操作，目前主要是预览流（<c>/stream</c>）：正常结果是 M-JPEG 流，
/// 无法提供流时只能抛出本异常。宿主层（Host）捕获后取出 <see cref="Error"/>，转换为
/// <see cref="ErrorResponse"/> 返回调用方。
/// </para>
/// <para>
/// 采集（<c>/capture</c>、<c>/rcapture</c>）等能在响应中逐项携带错误的操作不应使用本异常，
/// 而应把错误放进对应的 <see cref="BiometricInfo.Error"/>。
/// </para>
/// </remarks>
public class SbiException : Exception
{
    /// <summary>
    /// 构造携带 SBI 错误对象的异常
    /// </summary>
    /// <param name="error">SBI 错误对象，见 <see cref="SbiErrorHelper"/></param>
    /// <param name="innerException">内部异常</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> 为 null</exception>
    public SbiException(SbiError error, Exception? innerException = null)
        : base(error?.ErrorInfo, innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>
    /// SBI 错误对象
    /// </summary>
    public SbiError Error { get; }
}
