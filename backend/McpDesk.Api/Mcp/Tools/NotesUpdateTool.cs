using System.Text;
using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：修改笔记。
///
/// 采用「部分更新」语义：只覆盖客户端明确传入的字段，
/// 这样模型可以只改标题、或只追加内容，而不必先读出全文再整体写回。
/// </summary>
public sealed class NotesUpdateTool : IMcpTool
{
    private readonly NoteStore _store;

    /// <summary>构造函数：注入笔记存储。</summary>
    public NotesUpdateTool(NoteStore store)
    {
        _store = store;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "notes_update",
        Title = "修改笔记",
        Description =
            "按 ID 修改已有笔记。title、content、tags 传哪个就改哪个，未传的字段保持不变，" +
            "因此可以只改标题或只替换正文。至少要传一个要修改的字段。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("id", "要修改的笔记 ID（8 位十六进制，如 a1b2c3d4）。", required: true, minLength: 1, maxLength: 32)
            .AddString("title", "新的标题；不传则保持原样。", maxLength: 120)
            .AddString("content", "新的正文（整体替换）；不传则保持原样。", maxLength: 50000)
            .AddStringArray("tags", "新的标签列表（整体替换）；不传则保持原样。", maxItems: 10)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var id = args.RequiredString("id", "笔记 ID").Trim();

        var title = args.OptionalString("title");
        var content = args.OptionalString("content");
        var tags = args.Has("tags") ? args.OptionalStringList("tags") : null;

        if (title is null && content is null && tags is null)
        {
            throw new ToolException("没有要修改的字段：请至少传入 title、content、tags 中的一个。");
        }

        var note = _store.Update(id, title, content, tags)
                   ?? throw new ToolException($"笔记不存在：{id}。可先用 notes_list 查询正确的 ID。");

        var changed = new List<string>();
        if (title is not null) changed.Add("标题");
        if (content is not null) changed.Add("正文");
        if (tags is not null) changed.Add("标签");

        var builder = new StringBuilder();
        builder.AppendLine($"笔记 `{note.Id}` 已更新（{string.Join("、", changed)}）。");
        builder.AppendLine($"- 标题：{note.Title}");
        builder.Append($"- 更新时间：{note.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");

        var meta = new JsonObject
        {
            ["id"] = note.Id,
            ["updatedFields"] = new JsonArray(changed.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
        };

        return Task.FromResult(McpToolResult.Ok(builder.ToString(), meta));
    }
}
