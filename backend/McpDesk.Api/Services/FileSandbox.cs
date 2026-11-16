using Microsoft.Extensions.Options;

namespace McpDesk.Api.Services;

/// <summary>
/// 文件访问沙盒：所有文件类工具都必须经过这里解析路径，
/// 保证 AI 只能访问用户明确授权的目录（默认是当前用户目录）。
///
/// 这是本项目最重要的安全边界之一：
///   模型可能被对话内容误导，尝试读取 C:\Windows 或其它用户的数据；
///   通过统一的路径校验，可以确保越界访问直接报错，而不是悄悄放行。
/// </summary>
public sealed class FileSandbox
{
    private readonly string _root;

    /// <summary>构造函数：确定沙盒根目录。</summary>
    public FileSandbox(IOptions<AppOptions> options, ILogger<FileSandbox> logger)
    {
        var configured = options.Value.FileSandboxRoot;

        var root = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : configured;

        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        logger.LogInformation("文件沙盒根目录：{Root}", _root);
    }

    /// <summary>沙盒根目录（绝对路径，无末尾分隔符）。</summary>
    public string Root => _root;

    /// <summary>
    /// 把客户端传来的相对路径解析为绝对路径，并校验没有越界。
    /// </summary>
    /// <param name="path">相对路径（相对于沙盒根目录），也允许传根目录下的绝对路径。</param>
    /// <returns>规范化后的绝对路径。</returns>
    /// <exception cref="Mcp.ToolException">路径越界或格式非法。</exception>
    public string Resolve(string? path)
    {
        var raw = (path ?? string.Empty).Trim();

        // 统一分隔符，避免个别客户端传 / 或混合斜杠
        raw = raw.Replace('/', Path.DirectorySeparatorChar);

        string combined;
        try
        {
            combined = string.IsNullOrEmpty(raw)
                ? _root
                : Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(_root, raw);

            combined = Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new Mcp.ToolException($"路径格式不合法：{path}");
        }

        // 越界判断：必须是根目录本身，或以「根目录 + 分隔符」开头
        var normalizedRoot = _root + Path.DirectorySeparatorChar;
        var isInside = combined.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
                       combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);

        if (!isInside)
        {
            throw new Mcp.ToolException(
                $"拒绝访问：{combined} 不在允许的目录内。当前沙盒根目录为 {_root}，请使用相对路径。");
        }

        return combined;
    }

    /// <summary>把绝对路径转成相对沙盒根目录的展示形式（用于结果输出）。</summary>
    public string ToDisplayPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_root, fullPath);
        return relative == "." ? "(根目录)" : relative;
    }
}
