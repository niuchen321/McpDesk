using System.Text;
using System.Text.Json.Nodes;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：列出目录内容。
///
/// 只能浏览 <see cref="FileSandbox"/> 授权范围内的目录（默认是当前用户主目录），
/// 越界路径会直接报错。
/// </summary>
public sealed class ListFilesTool : IMcpTool
{
    private readonly FileSandbox _sandbox;

    /// <summary>构造函数：注入文件沙盒。</summary>
    public ListFilesTool(FileSandbox sandbox)
    {
        _sandbox = sandbox;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "list_files",
        Title = "浏览目录",
        Description =
            "列出沙盒目录下某个子目录的文件与文件夹（默认是用户主目录），可按通配符过滤。" +
            "返回文件名、类型、大小与修改时间；不会读取文件内容。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("path", "相对沙盒根目录的路径，例如 Documents、Desktop\\项目；留空表示根目录。")
            .AddString("pattern", "文件名通配符，默认 *（全部），例如 *.cs、*.md。", defaultValue: "*", maxLength: 60)
            .AddBoolean("recursive", "是否递归子目录，默认 false。", defaultValue: false)
            .AddInteger("limit", "最多返回多少个条目（1～500），默认 100。", defaultValue: 100, minimum: 1, maximum: 500)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var path = args.OptionalString("path");
        var pattern = args.OptionalString("pattern", "*") ?? "*";
        var recursive = args.OptionalBool("recursive");
        var limit = args.OptionalInt("limit", 100, 1, 500) ?? 100;

        var directory = _sandbox.Resolve(path);

        if (!Directory.Exists(directory))
        {
            throw new ToolException($"目录不存在：{_sandbox.ToDisplayPath(directory)}");
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // 先取子目录，再取文件，最后的列表是「目录在前、文件在后」
        var directories = Directory.EnumerateDirectories(directory, "*", option)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        var remaining = Math.Max(0, limit - directories.Count);
        var files = Directory.EnumerateFiles(directory, pattern, option)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(remaining + 1) // 多取一个，用于判断是否被截断
            .ToList();

        var truncated = files.Count > remaining;
        if (truncated)
        {
            files = files.Take(remaining).ToList();
        }

        var builder = new StringBuilder();
        builder.AppendLine($"目录：{_sandbox.ToDisplayPath(directory)}");
        builder.AppendLine($"过滤：{pattern}，共 {directories.Count + files.Count} 个条目"
                           + (truncated ? "（已达上限，结果被截断）" : string.Empty));
        builder.AppendLine();

        foreach (var dir in directories)
        {
            builder.AppendLine($"[目录] {_sandbox.ToDisplayPath(dir)}");
        }

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                builder.AppendLine(
                    $"[文件] {_sandbox.ToDisplayPath(file)}  ({FormatSize(info.Length)}，{info.LastWriteTime:yyyy-MM-dd HH:mm})");
            }
            catch (IOException)
            {
                // 文件在枚举后被删除或正在被占用，跳过即可
            }
        }

        var meta = new JsonObject
        {
            ["directory"] = _sandbox.ToDisplayPath(directory),
            ["directoryCount"] = directories.Count,
            ["fileCount"] = files.Count,
            ["truncated"] = truncated,
        };

        return Task.FromResult(McpToolResult.Ok(builder.ToString().TrimEnd(), meta));
    }

    /// <summary>把字节数格式化为 B / KB / MB / GB。</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
    };
}
