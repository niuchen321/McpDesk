using System.Text.Json.Nodes;

namespace McpDesk.Api.Models;

/// <summary>
/// 一条工具调用记录，仅保存在内存中（环形缓冲），用于前端「调用日志」面板。
///
/// 刻意不落盘：日志属于调试信息，重启即清空；真正需要留存的业务数据是笔记。
/// </summary>
public sealed class CallLogEntry
{
    /// <summary>自增序号，前端用作列表 key。</summary>
    public long Id { get; set; }

    /// <summary>调用的工具名，如 notes_create。</summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>本次调用传入的参数（原样保留，便于复现）。</summary>
    public JsonObject Arguments { get; set; } = new();

    /// <summary>调用是否返回错误。</summary>
    public bool IsError { get; set; }

    /// <summary>结果文本的前若干字符（过长会截断，避免日志过大）。</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>执行耗时（毫秒）。</summary>
    public double ElapsedMs { get; set; }

    /// <summary>调用发生的本地时间。</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>调用来源：mcp（外部 MCP 客户端）或 api（管理后台）。</summary>
    public string Source { get; set; } = "mcp";
}
