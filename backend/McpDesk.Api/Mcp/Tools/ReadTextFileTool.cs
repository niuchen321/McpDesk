using System.Text;
using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：读取文本文件内容。
///
/// 安全设计：
///   1. 路径必须经过 <see cref="FileSandbox"/> 校验，禁止越界读取；
///   2. 只读取文本类扩展名，避免把二进制文件读进来变成乱码；
///   3. 限制单次读取的字节数与行数，防止超大文件撑爆上下文。
/// </summary>
public sealed class ReadTextFileTool : IMcpTool
{
    /// <summary>允许读取的文本文件扩展名白名单。</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".razor", ".cshtml",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".css", ".scss", ".less", ".html", ".htm",
        ".py", ".java", ".kt", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc", ".m", ".mm",
        ".php", ".rb", ".swift", ".lua", ".sql", ".sh", ".ps1", ".bat", ".cmd", ".dockerfile",
        ".gitignore", ".gitattributes", ".editorconfig", ".env", ".log", ".csv", ".tsv", ".properties",
    };

    /// <summary>不含扩展名但通常为文本的文件名。</summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "Makefile", "README", "LICENSE", "CHANGELOG", "NOTICE", ".gitignore", ".editorconfig",
    };

    private const long MaxFileBytes = 512 * 1024;

    private readonly FileSandbox _sandbox;

    /// <summary>构造函数：注入文件沙盒。</summary>
    public ReadTextFileTool(FileSandbox sandbox)
    {
        _sandbox = sandbox;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "read_text_file",
        Title = "读取文本文件",
        Description =
            "读取沙盒目录内某个文本文件的内容，可以只读取指定行范围。" +
            "仅支持常见文本扩展名（.txt/.md/.json/.cs/.js/.py 等），单个文件最大 512 KB。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("path", "相对沙盒根目录的文件路径，例如 Documents\\笔记.md。", required: true)
            .AddInteger("startLine", "从第几行开始读（从 1 开始），默认 1。", defaultValue: 1, minimum: 1)
            .AddInteger("lineCount", "最多读取多少行（1～2000），默认 500。", defaultValue: 500, minimum: 1, maximum: 2000)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var path = args.RequiredString("path", "文件路径");
        var startLine = args.OptionalInt("startLine", 1, 1) ?? 1;
        var lineCount = args.OptionalInt("lineCount", 500, 1, 2000) ?? 500;

        var fullPath = _sandbox.Resolve(path);

        if (!File.Exists(fullPath))
        {
            throw new ToolException($"文件不存在：{_sandbox.ToDisplayPath(fullPath)}");
        }

        var extension = Path.GetExtension(fullPath);
        var fileName = Path.GetFileName(fullPath);
        var isText = TextExtensions.Contains(extension) || TextFileNames.Contains(fileName);

        if (!isText)
        {
            throw new ToolException(
                $"拒绝读取非文本文件：{_sandbox.ToDisplayPath(fullPath)}。" +
                "当前只允许读取常见文本扩展名，如需读取请先用其它方式转换。");
        }

        var info = new FileInfo(fullPath);
        if (info.Length > MaxFileBytes)
        {
            throw new ToolException(
                $"文件过大（{info.Length / 1024} KB），超过 512 KB 上限：" +
                $"{_sandbox.ToDisplayPath(fullPath)}。请改用 startLine / lineCount 读取其中的一部分。");
        }

        // 使用 FileShare.ReadWrite，避免文件同时被编辑器写锁时读取失败
        string[] lines;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var all = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                all.Add(line);
            }

            lines = all.ToArray();
        }

        var totalLines = lines.Length;
        var skip = Math.Min(startLine - 1, totalLines);
        var selected = lines.Skip(skip).Take(lineCount).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"文件：{_sandbox.ToDisplayPath(fullPath)}");
        builder.AppendLine($"大小：{FormatSize(info.Length)}，总行数：{totalLines}，" +
                           $"本次读取第 {skip + 1}～{skip + selected.Count} 行");
        builder.AppendLine(new string('-', 40));

        for (var i = 0; i < selected.Count; i++)
        {
            // 行号右对齐，方便模型引用具体位置
            builder.AppendLine($"{skip + i + 1,5} | {selected[i]}");
        }

        if (skip + selected.Count < totalLines)
        {
            builder.AppendLine(new string('-', 40));
            builder.AppendLine($"（还有 {totalLines - skip - selected.Count} 行未显示，可调大 lineCount 或从 startLine 继续读取）");
        }

        var meta = new JsonObject
        {
            ["path"] = _sandbox.ToDisplayPath(fullPath),
            ["sizeBytes"] = info.Length,
            ["totalLines"] = totalLines,
            ["startLine"] = skip + 1,
            ["returnedLines"] = selected.Count,
            ["lastWriteTime"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        return Task.FromResult(McpToolResult.Ok(builder.ToString().TrimEnd(), meta));
    }

    /// <summary>把字节数格式化为 B / KB / MB。</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1024.0 / 1024.0:F2} MB",
    };
}
