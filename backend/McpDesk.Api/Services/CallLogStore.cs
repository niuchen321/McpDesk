using System.Text.Json.Nodes;
using McpDesk.Api.Models;
using Microsoft.Extensions.Options;

namespace McpDesk.Api.Services;

/// <summary>
/// 工具调用日志（内存环形缓冲）。
///
/// 用途：
///   · 前端「调用日志」面板实时查看最近发生了什么；
///   · 调试 MCP 客户端时确认参数是否正确传递；
///   · 排查工具耗时。
///
/// 实现：List + 锁，超过上限时从最旧的记录开始丢弃。个人使用场景下无需引入队列库。
/// </summary>
public sealed class CallLogStore
{
    private readonly object _gate = new();
    private readonly LinkedList<CallLogEntry> _entries = new();
    private readonly int _maxEntries;
    private long _sequence;

    /// <summary>构造函数：从配置读取保留条数。</summary>
    public CallLogStore(IOptions<AppOptions> options)
    {
        _maxEntries = Math.Clamp(options.Value.MaxLogEntries, 10, 5000);
    }

    /// <summary>
    /// 追加一条日志。
    /// </summary>
    /// <param name="tool">工具名。</param>
    /// <param name="arguments">调用参数（会被复制，避免外部改动影响日志）。</param>
    /// <param name="isError">是否失败。</param>
    /// <param name="text">结果文本，内部会截断预览。</param>
    /// <param name="elapsedMs">耗时（毫秒）。</param>
    /// <param name="source">来源，mcp 或 api。</param>
    public void Add(
        string tool,
        JsonObject arguments,
        bool isError,
        string text,
        double elapsedMs,
        string source)
    {
        lock (_gate)
        {
            var entry = new CallLogEntry
            {
                Id = ++_sequence,
                Tool = tool,
                Arguments = (JsonObject)(arguments.DeepClone() ?? new JsonObject()),
                IsError = isError,
                Preview = Truncate(text, 300),
                ElapsedMs = Math.Round(elapsedMs, 2),
                Timestamp = DateTimeOffset.Now,
                Source = source,
            };

            _entries.AddLast(entry);
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveFirst();
            }
        }
    }

    /// <summary>取最近 N 条日志，按时间倒序（最新的在前）。</summary>
    public IReadOnlyList<CallLogEntry> Recent(int limit)
    {
        lock (_gate)
        {
            return _entries
                .Reverse()
                .Take(Math.Clamp(limit, 1, _maxEntries))
                .ToList();
        }
    }

    /// <summary>清空日志（前端「清空」按钮调用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>按字符数截断文本，超出部分用省略号表示。</summary>
    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }
}
