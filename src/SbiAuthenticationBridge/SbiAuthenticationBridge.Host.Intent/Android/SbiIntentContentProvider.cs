using Android.Content;
using Android.Database;
using Android.OS;
using AndroidUri = Android.Net.Uri;
using Serilog;

namespace SbiAuthenticationBridge.Host.Intent.Android;

/// <summary>
/// 为采集响应提供 <c>content://</c> 读取能力
/// </summary>
/// <remarks>
/// <para>
/// Android SBI 规范要求采集结果通过 content URI 返回，调用方读取后立即失效。
/// 本提供者按一次性标识提供只读流，不落盘；读取后条目即从内存移除。
/// </para>
/// <para>
/// 按规范不实现 insert/update/delete，相关调用直接抛出
/// <see cref="Java.Lang.UnsupportedOperationException"/>。
/// </para>
/// </remarks>
[ContentProvider(new[] { SbiIntentActions.ContentProviderAuthority },
    Exported = false,
    GrantUriPermissions = true)]
public sealed class SbiIntentContentProvider : ContentProvider
{
    private const string JsonMimeType = "application/json";

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Intent");

    /// <summary>
    /// 初始化提供者
    /// </summary>
    /// <returns>始终返回 <c>true</c></returns>
    public override bool OnCreate() => true;

    /// <summary>
    /// 打开一次性只读响应流
    /// </summary>
    /// <param name="uri">content URI</param>
    /// <param name="mode">打开模式，仅支持只读</param>
    /// <returns>读取端文件描述符</returns>
    /// <exception cref="Java.IO.FileNotFoundException">标识不存在、已过期或已被读取</exception>
    public override ParcelFileDescriptor OpenFile(AndroidUri uri, string mode)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var payload = SbiIntentResponseStore.Take(uri.LastPathSegment)
                      ?? throw new Java.IO.FileNotFoundException($"SBI 响应不存在或已被读取：{uri}");

        var pipe = ParcelFileDescriptor.CreatePipe();

        if (pipe is not { Length: >= 2 })
        {
            throw new Java.Lang.IllegalStateException("无法创建 SBI 响应管道。");
        }

        var read = pipe[0];
        var write = pipe[1];

        _ = Task.Run(() =>
        {
            try
            {
                using var output = new ParcelFileDescriptor.AutoCloseOutputStream(write);
                output.Write(payload, 0, payload.Length);
            }
            catch (Exception exception)
            {
                // 调用方提前关闭流或管道中断时无需处理，内容已无法再读取
                Logger.Warning(exception, "写入 SBI 响应失败");
            }
        });

        return read;
    }

    /// <summary>
    /// 返回响应 MIME 类型
    /// </summary>
    public override string GetType(AndroidUri uri) => JsonMimeType;

    /// <summary>
    /// 不支持查询
    /// </summary>
    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection,
        string[]? selectionArgs, string? sortOrder) => null;

    /// <summary>
    /// 不支持插入
    /// </summary>
    public override AndroidUri Insert(AndroidUri uri, ContentValues? values) =>
        throw new Java.Lang.UnsupportedOperationException("SBI 响应提供者不支持插入。");

    /// <summary>
    /// 不支持删除；读取即自动失效
    /// </summary>
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) =>
        throw new Java.Lang.UnsupportedOperationException("SBI 响应提供者不支持删除。");

    /// <summary>
    /// 不支持更新
    /// </summary>
    public override int Update(AndroidUri uri, ContentValues? values, string? selection,
        string[]? selectionArgs) =>
        throw new Java.Lang.UnsupportedOperationException("SBI 响应提供者不支持更新。");
}
