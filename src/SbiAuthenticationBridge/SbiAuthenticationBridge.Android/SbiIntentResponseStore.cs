using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// 采集响应文件的落盘与解析；响应通过 content URI 交给调用方
/// </summary>
internal static class SbiIntentResponseStore
{
    /// <summary>
    /// 缓存目录名
    /// </summary>
    private const string DirectoryName = "sbi-intent";

    /// <summary>
    /// 写入响应文件并返回可读取的 content URI
    /// </summary>
    /// <param name="context">上下文</param>
    /// <param name="authority">ContentProvider authority</param>
    /// <param name="json">响应 JSON</param>
    /// <returns>content URI</returns>
    public static global::Android.Net.Uri Write(
        global::Android.Content.Context context,
        string authority,
        string json)
    {
        var directory = EnsureDirectory(context);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, id + ".json");

        File.WriteAllText(path, json, Encoding.UTF8);
        return global::Android.Net.Uri.Parse($"content://{authority}/{id}")
            ?? throw new InvalidOperationException("构造响应 content URI 失败。");
    }

    /// <summary>
    /// 把 content URI 解析为本地文件路径
    /// </summary>
    /// <param name="context">上下文</param>
    /// <param name="uri">content URI</param>
    /// <returns>文件路径；URI 非法或文件不存在时为 <see langword="null"/></returns>
    public static string? Resolve(global::Android.Content.Context context, global::Android.Net.Uri uri)
    {
        var id = uri.LastPathSegment;

        if (string.IsNullOrEmpty(id) || id.Any(static c => !char.IsAsciiHexDigit(c)))
        {
            return null;
        }

        var path = Path.Combine(EnsureDirectory(context), id + ".json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 确保缓存目录存在
    /// </summary>
    private static string EnsureDirectory(global::Android.Content.Context context)
    {
        var directory = Path.Combine(context.CacheDir!.AbsolutePath, DirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
