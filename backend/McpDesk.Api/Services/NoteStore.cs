using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using McpDesk.Api.Models;
using Microsoft.Extensions.Options;

namespace McpDesk.Api.Services;

/// <summary>
/// 个人笔记的存储与检索。
///
/// 存储方案：单个 JSON 文件（默认 data/notes.json）。
///   · 优点：零依赖、可直接阅读/备份/纳入版本管理；
///   · 一致性：所有读写都在 _gate 锁内完成，并用「写临时文件再替换」的方式避免半截文件；
///   · 规模：单机个人使用（数千条以内）性能充足，无需数据库。
///
/// 生命周期：注册为单例，进程内共享同一份缓存。
/// </summary>
public sealed class NoteStore
{
    private readonly object _gate = new();
    private readonly ILogger<NoteStore> _logger;
    private readonly string _filePath;
    private List<Note>? _cache;

    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 让中文在 JSON 文件里保持可读，而不是被转义成 \uXXXX
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>构造函数：确定存储文件位置（目录不存在会自动创建）。</summary>
    public NoteStore(
        IOptions<AppOptions> options,
        IHostEnvironment environment,
        ILogger<NoteStore> logger)
    {
        _logger = logger;

        var root = options.Value.StorageRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = "data";
        }

        // 相对路径以「内容根目录」为基准（dotnet run 时就是项目目录）
        var directory = Path.IsPathRooted(root)
            ? root
            : Path.Combine(environment.ContentRootPath, root);

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "notes.json");
    }

    /// <summary>数据文件完整路径（管理后台展示用）。</summary>
    public string FilePath => _filePath;

    /// <summary>返回全部笔记，按更新时间倒序（最近改动的在前）。</summary>
    public IReadOnlyList<Note> GetAll()
    {
        lock (_gate)
        {
            return Load().OrderByDescending(n => n.UpdatedAt).ToList();
        }
    }

    /// <summary>按 ID 查询单条笔记，不存在返回 null。</summary>
    public Note? Get(string id)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(n => n.Id == id);
        }
    }

    /// <summary>
    /// 关键字检索：标题、正文、标签任一命中即可（不区分大小写）。
    /// </summary>
    /// <param name="keyword">关键字；为空时表示不过滤。</param>
    /// <param name="tag">按标签过滤；为空时表示不过滤。</param>
    /// <param name="limit">最多返回条数。</param>
    public IReadOnlyList<Note> Search(string? keyword, string? tag, int limit)
    {
        lock (_gate)
        {
            IEnumerable<Note> query = Load();

            if (!string.IsNullOrWhiteSpace(tag))
            {
                query = query.Where(n => n.Tags.Any(t => t.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(n =>
                    n.Title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    n.Content.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    n.Tags.Any(t => t.Contains(k, StringComparison.OrdinalIgnoreCase)));
            }

            return query
                .OrderByDescending(n => n.UpdatedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .ToList();
        }
    }

    /// <summary>创建笔记并立即落盘。</summary>
    public Note Create(string title, string content, IEnumerable<string>? tags)
    {
        lock (_gate)
        {
            var notes = Load();
            var now = DateTimeOffset.UtcNow;

            var note = new Note
            {
                Id = NewId(),
                Title = Normalize(title, 120, "未命名笔记"),
                Content = content ?? string.Empty,
                Tags = NormalizeTags(tags),
                CreatedAt = now,
                UpdatedAt = now,
            };

            notes.Add(note);
            Save(notes);
            return note;
        }
    }

    /// <summary>
    /// 更新笔记。只有传入非 null 的字段才会被覆盖，未传的字段保持原值。
    /// </summary>
    /// <returns>更新后的笔记；ID 不存在时返回 null。</returns>
    public Note? Update(string id, string? title = null, string? content = null, IEnumerable<string>? tags = null)
    {
        lock (_gate)
        {
            var notes = Load();
            var note = notes.FirstOrDefault(n => n.Id == id);
            if (note is null)
            {
                return null;
            }

            if (title is not null)
            {
                note.Title = Normalize(title, 120, note.Title);
            }

            if (content is not null)
            {
                note.Content = content;
            }

            if (tags is not null)
            {
                note.Tags = NormalizeTags(tags);
            }

            note.UpdatedAt = DateTimeOffset.UtcNow;
            Save(notes);
            return note;
        }
    }

    /// <summary>删除笔记；返回是否真的删除了。</summary>
    public bool Delete(string id)
    {
        lock (_gate)
        {
            var notes = Load();
            var removed = notes.RemoveAll(n => n.Id == id) > 0;
            if (removed)
            {
                Save(notes);
            }

            return removed;
        }
    }

    /// <summary>生成 8 位十六进制 ID；与现有 ID 冲突时重新生成。</summary>
    private string NewId()
    {
        var notes = Load();
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N")[..8];
        }
        while (notes.Any(n => n.Id == id));

        return id;
    }

    /// <summary>懒加载：首次访问时从磁盘读取，之后直接使用内存缓存。</summary>
    private List<Note> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_filePath))
        {
            _cache = new List<Note>();
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _cache = JsonSerializer.Deserialize<List<Note>>(json, FileJsonOptions) ?? new List<Note>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 文件损坏时不覆盖原文件，先备份，再从空数据开始，避免用户数据被静默清空
            _logger.LogError(ex, "读取笔记文件失败：{Path}", _filePath);
            var backup = $"{_filePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(_filePath, backup, overwrite: true);
            }
            catch (IOException)
            {
                // 备份失败不影响继续运行
            }

            _cache = new List<Note>();
        }

        return _cache;
    }

    /// <summary>
    /// 原子写入：先写 .tmp，再用 File.Move 覆盖目标文件。
    /// 这样即使写入过程中断电，也不会留下半截 JSON。
    /// </summary>
    private void Save(List<Note> notes)
    {
        var json = JsonSerializer.Serialize(notes, FileJsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>规范化标题：去首尾空白、截断超长内容、空值给默认标题。</summary>
    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            text = fallback;
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>规范化标签：去空、去重（忽略大小写）、最多 10 个、每个不超过 24 字符。</summary>
    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return new List<string>();
        }

        return tags
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 24 ? t[..24] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }
}
