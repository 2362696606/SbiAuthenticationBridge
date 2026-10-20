using System.Text;
using AndroidIntent = Android.Content.Intent;

namespace SbiAuthenticationBridge.Host.Intent.Android;

/// <summary>
/// Intent extra 的读取辅助
/// </summary>
internal static class SbiIntentExtras
{
    /// <summary>
    /// 读取 <c>input</c> extra：按规范是字节数组，同时兼容以字符串形式传入的调用方
    /// </summary>
    /// <param name="intent">调用方 Intent</param>
    /// <returns>UTF-8 JSON 字节；未提供时返回 <c>null</c></returns>
    internal static byte[]? ReadInput(AndroidIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!intent.HasExtra(SbiIntentActions.InputExtra))
        {
            return null;
        }

        var bytes = intent.GetByteArrayExtra(SbiIntentActions.InputExtra);
        if (bytes is { Length: > 0 })
        {
            return bytes;
        }

        var text = intent.GetStringExtra(SbiIntentActions.InputExtra);
        return string.IsNullOrEmpty(text) ? null : Encoding.UTF8.GetBytes(text);
    }
}
