using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：删除笔记。
///
/// 删除不可恢复，因此返回内容里会带上被删笔记的标题，方便用户在客户端确认操作是否正确。
/// </summary>
public sealed class NotesDeleteTool : IMcpTool
{
    private readonly NoteStore _store;

    /// <summary>构造函数：注入笔记存储。</summary>
    public NotesDeleteTool(NoteStore store)
    {
        _store = store;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "notes_delete",
        Title = "删除笔记",
        Description =
            "按 ID 删除一条笔记，删除后无法恢复。调用前建议先用 notes_list 确认目标笔记，" +
            "并在结果中向用户复述被删除的标题。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("id", "要删除的笔记 ID（8 位十六进制）。", required: true, minLength: 1, maxLength: 32)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var id = args.RequiredString("id", "笔记 ID").Trim();

        // 先读出来，用于结果展示（删除后就没有了）
        var note = _store.Get(id) ?? throw new ToolException($"笔记不存在：{id}。可先用 notes_list 查询正确的 ID。");

        var deleted = _store.Delete(id);
        if (!deleted)
        {
            // 理论上不会发生（上面已确认存在），并发情况下仍兜底
            throw new ToolException($"删除失败：笔记 {id} 可能已被其它操作删除。");
        }

        var meta = new JsonObject
        {
            ["id"] = note.Id,
            ["title"] = note.Title,
        };

        return Task.FromResult(McpToolResult.Ok($"已删除笔记「{note.Title}」（ID：{note.Id}）。", meta));
    }
}
