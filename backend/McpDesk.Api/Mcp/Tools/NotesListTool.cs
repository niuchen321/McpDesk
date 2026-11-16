using System.Text;
using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：列出 / 检索个人笔记。
///
/// 同时支持两种用法：
///   · 不传参数：查看最近的笔记；
///   · 传 keyword 或 tag：按关键字、标签过滤。
/// 返回内容为 Markdown 文本，便于模型直接阅读与引用。
/// </summary>
public sealed class NotesListTool : IMcpTool
{
    private readonly NoteStore _store;

    /// <summary>构造函数：注入笔记存储。</summary>
    public NotesListTool(NoteStore store)
    {
        _store = store;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "notes_list",
        Title = "列出或检索笔记",
        Description =
            "列出个人笔记，可按关键字（标题/正文/标签）或标签过滤，按更新时间倒序返回。" +
            "每条笔记都带有 8 位 ID，可用于 notes_update、notes_delete 或读取正文。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("keyword", "关键字，匹配标题、正文或标签；留空表示不过滤。", maxLength: 100)
            .AddString("tag", "只返回带有该标签的笔记；留空表示不过滤。", maxLength: 24)
            .AddInteger("limit", "最多返回多少条（1～100），默认 20。", defaultValue: 20, minimum: 1, maximum: 100)
            .AddBoolean("includeContent", "是否在结果中附带正文（最多 200 字）。", defaultValue: false)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var keyword = args.OptionalString("keyword");
        var tag = args.OptionalString("tag");
        var limit = args.OptionalInt("limit", 20, 1, 100) ?? 20;
        var includeContent = args.OptionalBool("includeContent");

        var notes = _store.Search(keyword, tag, limit);

        if (notes.Count == 0)
        {
            var reason = !string.IsNullOrWhiteSpace(keyword) || !string.IsNullOrWhiteSpace(tag)
                ? "没有符合条件的笔记。"
                : "目前还没有任何笔记，可以用 notes_create 创建第一条。";
            return Task.FromResult(McpToolResult.Ok(reason, new JsonObject { ["count"] = 0 }));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"共 {notes.Count} 条笔记（按更新时间倒序）：");
        builder.AppendLine();

        foreach (var note in notes)
        {
            builder.AppendLine($"### {note.Title}");
            builder.AppendLine($"- ID：`{note.Id}`");
            builder.AppendLine($"- 标签：{(note.Tags.Count > 0 ? string.Join("、", note.Tags) : "（无）")}");
            builder.AppendLine($"- 更新时间：{note.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");

            if (includeContent)
            {
                var preview = note.Content.Replace("\r\n", "\n");
                if (preview.Length > 200)
                {
                    preview = preview[..200] + "…";
                }

                builder.AppendLine($"- 正文：{preview}");
            }

            builder.AppendLine();
        }

        var meta = new JsonObject
        {
            ["count"] = notes.Count,
            ["ids"] = new JsonArray(notes.Select(n => (JsonNode?)JsonValue.Create(n.Id)).ToArray()),
        };

        return Task.FromResult(McpToolResult.Ok(builder.ToString().TrimEnd(), meta));
    }
}
