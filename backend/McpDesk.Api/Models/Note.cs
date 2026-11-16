namespace McpDesk.Api.Models;

/// <summary>
/// 一条个人笔记。
///
/// 说明：本项目为了「零第三方依赖」，使用 JSON 文件而不是数据库存储。
/// 数据量在几千条以内完全够用，且方便直接打开查看、备份、放进 Git。
/// </summary>
public sealed class Note
{
    /// <summary>笔记 ID，8 位十六进制短码（由服务端生成，客户端不要自行指定）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>标题，长度 1～120。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>正文，支持 Markdown 原文（服务端不做渲染，交由展示端处理）。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>标签列表，便于分类与检索。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
