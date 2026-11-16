using System.Text;
using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：创建笔记。
///
/// 典型用途：把对话中产生的灵感、待办、会议纪要直接落到本机笔记里，
/// 下次对话可以通过 notes_list 找回。
/// </summary>
public sealed class NotesCreateTool : IMcpTool
{
    private readonly NoteStore _store;

    /// <summary>构造函数：注入笔记存储。</summary>
    public NotesCreateTool(NoteStore store)
    {
        _store = store;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "notes_create",
        Title = "创建笔记",
        Description =
            "新建一条个人笔记并保存到本机。正文支持 Markdown；返回新笔记的 ID，" +
            "后续可用 notes_update / notes_delete 修改或删除。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("title", "笔记标题，不超过 120 字；留空时自动使用「未命名笔记」。", maxLength: 120)
            .AddString("content", "笔记正文（Markdown），不能为空。", required: true, maxLength: 50000)
            .AddStringArray("tags", "标签，最多 10 个，每个不超过 24 字。", maxItems: 10)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var content = args.RequiredString("content", "正文");
        var title = args.OptionalString("title") ?? "未命名笔记";
        var tags = args.OptionalStringList("tags");

        var note = _store.Create(title, content, tags);

        var builder = new StringBuilder();
        builder.AppendLine("笔记已创建。");
        builder.AppendLine($"- ID：`{note.Id}`");
        builder.AppendLine($"- 标题：{note.Title}");
        builder.AppendLine($"- 标签：{(note.Tags.Count > 0 ? string.Join("、", note.Tags) : "（无）")}");
        builder.Append($"- 时间：{note.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");

        var meta = new JsonObject
        {
            ["id"] = note.Id,
            ["title"] = note.Title,
            ["tags"] = new JsonArray(note.Tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
        };

        return Task.FromResult(McpToolResult.Ok(builder.ToString(), meta));
    }
}
