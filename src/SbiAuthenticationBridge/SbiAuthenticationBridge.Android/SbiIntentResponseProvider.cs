using System;
using Android.Content;
using Android.Database;
using Android.OS;
using Java.IO;
using AndroidUri = Android.Net.Uri;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// 采集响应文件的只读 ContentProvider；调用方按 content URI 读取，关闭即删除
/// </summary>
[ContentProvider(
    [SbiIntentAppId.ResponseAuthority],
    Name = SbiIntentAppId.ProviderName,
    Exported = false,
    GrantUriPermissions = true)]
public sealed class SbiIntentResponseProvider : ContentProvider
{
    /// <summary>
    /// 响应内容类型
    /// </summary>
    private const string ContentType = "application/json";

    /// <inheritdoc />
    public override bool OnCreate() => true;

    /// <inheritdoc />
    public override ParcelFileDescriptor? OpenFile(AndroidUri uri, string mode)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!string.Equals(mode, "r", StringComparison.Ordinal))
        {
            throw new FileNotFoundException("Intent 响应只允许只读访问。");
        }

        var path = SbiIntentResponseStore.Resolve(Context!, uri);
        if (path is null)
        {
            throw new FileNotFoundException($"Intent 响应不存在：{uri}");
        }

        return ParcelFileDescriptor.Open(
            new File(path),
            ParcelFileMode.ReadOnly,
            // 带关闭监听时平台要求 Handler 非空，否则抛 "Handler must not be null"
            new Handler(Looper.MainLooper!),
            new DeleteOnCloseListener(path));
    }

    /// <inheritdoc />
    public override string? GetType(AndroidUri uri) => ContentType;

    /// <inheritdoc />
    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection,
        string[]? selectionArgs, string? sortOrder) =>
        throw new NotSupportedException("Intent 响应只支持按 URI 读取。");

    /// <inheritdoc />
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) =>
        throw new NotSupportedException("Intent 响应不支持写入。");

    /// <inheritdoc />
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) =>
        throw new NotSupportedException("Intent 响应不支持删除。");

    /// <inheritdoc />
    public override int Update(AndroidUri uri, ContentValues? values, string? selection,
        string[]? selectionArgs) =>
        throw new NotSupportedException("Intent 响应不支持更新。");

    /// <summary>
    /// 读取结束（文件描述符关闭）后删除响应文件
    /// </summary>
    private sealed class DeleteOnCloseListener(string path) : Java.Lang.Object,
        ParcelFileDescriptor.IOnCloseListener
    {
        /// <inheritdoc />
        public void OnClose(Java.IO.IOException? e)
        {
            try
            {
                var file = new File(path);
                if (file.Exists())
                {
                    file.Delete();
                }
            }
            catch (Java.IO.IOException)
            {
                // 删除失败不影响调用方读取结果
            }
        }
    }
}
