using System;
using System.Collections.Generic;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 日志内存缓冲：承载 Serilog Sink 的写入，并向日志视图提供快照与变更通知
/// </summary>
public interface ILogEntryStore
{
    /// <summary>
    /// 缓冲区最大条目数
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// 追加一条日志，供 Sink 调用
    /// </summary>
    /// <param name="entry">日志记录</param>
    void Append(LogEntryDto entry);

    /// <summary>
    /// 按时间顺序复制当前缓冲内容
    /// </summary>
    /// <returns>日志快照</returns>
    IReadOnlyList<LogEntryDto> Snapshot();

    /// <summary>
    /// 清空缓冲区
    /// </summary>
    void Clear();

    /// <summary>
    /// 新增日志时触发，可能来自任意线程
    /// </summary>
    event EventHandler<LogEntryDto>? EntryAdded;

    /// <summary>
    /// 缓冲区被清空时触发
    /// </summary>
    event EventHandler? Cleared;
}
