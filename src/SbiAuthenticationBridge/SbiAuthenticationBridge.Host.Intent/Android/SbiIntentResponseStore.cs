using System.Collections.Concurrent;

namespace SbiAuthenticationBridge.Host.Intent.Android;

/// <summary>
/// 采集响应的一次性内存存储：调用方读取 content URI 后即被移除
/// </summary>
/// <remarks>
/// 内容提供者只在进程内服务，不落盘。未被读取的条目在过期后被清理，避免长期占用内存。
/// </remarks>
internal static class SbiIntentResponseStore
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    /// <summary>
    /// 写入响应并返回一次性标识
    /// </summary>
    /// <param name="payload">UTF-8 JSON 响应</param>
    /// <returns>内容 URI 的最后一段</returns>
    internal static string Put(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        PurgeExpired();

        var id = Guid.NewGuid().ToString("N");
        Entries[id] = new Entry(payload, DateTimeOffset.UtcNow);
        return id;
    }

    /// <summary>
    /// 取出并移除响应；第二次读取同一标识会失败
    /// </summary>
    /// <param name="id">内容 URI 的最后一段</param>
    /// <returns>响应字节；不存在或已过期时返回 <c>null</c></returns>
    internal static byte[]? Take(string? id)
    {
        if (string.IsNullOrEmpty(id) || !Entries.TryRemove(id, out var entry))
        {
            return null;
        }

        return entry.CreatedAt + EntryLifetime < DateTimeOffset.UtcNow ? null : entry.Payload;
    }

    /// <summary>
    /// 清理过期条目
    /// </summary>
    private static void PurgeExpired()
    {
        var deadline = DateTimeOffset.UtcNow - EntryLifetime;

        foreach (var pair in Entries)
        {
            if (pair.Value.CreatedAt < deadline)
            {
                Entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(byte[] Payload, DateTimeOffset CreatedAt);
}
