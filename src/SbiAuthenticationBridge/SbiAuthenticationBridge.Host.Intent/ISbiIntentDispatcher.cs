using System.Threading;
using System.Threading.Tasks;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// SBI Intent 请求分发入口，由平台宿主调用
/// </summary>
public interface ISbiIntentDispatcher
{
    /// <summary>
    /// 处理一次 SBI Intent 请求
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="input">extras 中的 <c>input</c>（JSON 字节）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应结果；异常已收敛为 SBI 错误报文</returns>
    Task<SbiIntentResult> DispatchAsync(string action, byte[] input,
        CancellationToken cancellationToken = default);
}
