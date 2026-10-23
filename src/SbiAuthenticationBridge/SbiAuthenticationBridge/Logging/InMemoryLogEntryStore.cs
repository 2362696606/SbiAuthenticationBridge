using System;
using System.Collections.Generic;
using System.Threading;

namespace SbiAuthenticationBridge.Logging;

/// <summary>
/// 线程安全的有界环形缓冲，超出容量时丢弃最旧的日志
/// </summary>
internal sealed class InMemoryLogEntryStore : ILogEntryStore
{
    /// <summary>
    /// 默认缓冲区容量
    /// </summary>
    public const int DefaultCapacity = 2000;

    private readonly Lock _gate = new();

    private readonly LogEntryDto[] _buffer;

    private int _start;

    private int _count;

    /// <summary>
    /// 创建内存日志缓冲
    /// </summary>
    /// <param name="capacity">最大条目数</param>
    public InMemoryLogEntryStore(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        _buffer = new LogEntryDto[capacity];
    }

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public event EventHandler<LogEntryDto>? EntryAdded;

    /// <inheritdoc />
    public event EventHandler? Cleared;

    /// <inheritdoc />
    public void Append(LogEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            if (_count < Capacity)
            {
                _buffer[(_start + _count) % Capacity] = entry;
                _count++;
            }
            else
            {
                _buffer[_start] = entry;
                _start = (_start + 1) % Capacity;
            }
        }

        // 在锁外通知，避免订阅方回调时产生重入死锁
        EntryAdded?.Invoke(this, entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<LogEntryDto> Snapshot()
    {
        lock (_gate)
        {
            var entries = new LogEntryDto[_count];

            for (var index = 0; index < _count; index++)
            {
                entries[index] = _buffer[(_start + index) % Capacity];
            }

            return entries;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _start = 0;
            _count = 0;
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
