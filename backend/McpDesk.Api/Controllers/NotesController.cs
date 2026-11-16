using McpDesk.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpDesk.Api.Controllers;

/// <summary>
/// 笔记管理接口（供 Vue 管理后台使用）。
///
/// 数据与 MCP 工具（notes_list / notes_create / notes_update / notes_delete）共用
/// 同一个 <see cref="NoteStore"/> 单例，因此页面上改的内容会立刻被 AI 看到，反之亦然。
/// </summary>
[ApiController]
[Route("api/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly NoteStore _notes;

    /// <summary>构造函数：注入笔记存储。</summary>
    public NotesController(NoteStore notes)
    {
        _notes = notes;
    }

    /// <summary>
    /// 查询笔记列表。
    /// 支持按关键字（标题/正文/标签）和标签过滤，结果按更新时间倒序。
    /// </summary>
    [HttpGet]
    public IActionResult List([FromQuery] string? keyword, [FromQuery] string? tag, [FromQuery] int limit = 100)
    {
        var notes = _notes.Search(keyword, tag, limit);
        return Ok(new { count = notes.Count, notes });
    }

    /// <summary>按 ID 获取单条笔记。</summary>
    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var note = _notes.Get(id);
        return note is null
            ? NotFound(new { error = $"笔记不存在：{id}" })
            : Ok(note);
    }

    /// <summary>新建笔记。</summary>
    [HttpPost]
    public IActionResult Create([FromBody] NoteCreateRequest request)
    {
        var note = _notes.Create(request.Title ?? string.Empty, request.Content ?? string.Empty, request.Tags);
        // 201 Created + Location 头，符合 REST 惯例
        return CreatedAtAction(nameof(Get), new { id = note.Id }, note);
    }

    /// <summary>
    /// 更新笔记（部分更新）：请求里为 null 的字段保持原值。
    /// </summary>
    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] NoteUpdateRequest request)
    {
        var note = _notes.Update(id, request.Title, request.Content, request.Tags);
        return note is null
            ? NotFound(new { error = $"笔记不存在：{id}" })
            : Ok(note);
    }

    /// <summary>删除笔记。</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        return _notes.Delete(id)
            ? NoContent()
            : NotFound(new { error = $"笔记不存在：{id}" });
    }

    /// <summary>创建笔记的请求体。</summary>
    public sealed class NoteCreateRequest
    {
        /// <summary>标题，可省略（默认「未命名笔记」），最长 120 字符。</summary>
        public string? Title { get; set; }

        /// <summary>正文，可省略。</summary>
        public string? Content { get; set; }

        /// <summary>标签列表，最多 10 个。</summary>
        public List<string>? Tags { get; set; }
    }

    /// <summary>更新笔记的请求体：只为 null 表示不修改该字段。</summary>
    public sealed class NoteUpdateRequest
    {
        /// <summary>新标题；null 表示保持不变。</summary>
        public string? Title { get; set; }

        /// <summary>新正文；null 表示保持不变。</summary>
        public string? Content { get; set; }

        /// <summary>新标签；null 表示保持不变，空数组表示清空标签。</summary>
        public List<string>? Tags { get; set; }
    }
}
