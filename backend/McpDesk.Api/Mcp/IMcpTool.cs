using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp;

/// <summary>
/// 一个 MCP 工具的「身份证」：名称、标题、说明与输入参数的结构定义。
///
/// 其中 <see cref="InputSchema"/> 是一个 JSON Schema（draft 2020-12 子集），
/// MCP 客户端（以及本项目的 Vue 前端）会根据它自动生成调用表单、做参数校验提示。
/// </summary>
public sealed class McpToolDefinition
{
    /// <summary>
    /// 工具的唯一标识，供 tools/call 使用。
    /// 约定使用「小写 + 下划线」命名（如 notes_create），与 MCP 社区习惯一致。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 给人看的标题（中文），会显示在客户端的工具列表里。为空时回退到 <see cref="Name"/>。
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 工具用途说明。请写清楚「做什么、什么时候用」，模型依据这段文字决定是否调用本工具。
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 输入参数的 JSON Schema 对象（type = object）。
    /// 使用 <see cref="JsonSchemaBuilder"/> 构建，避免手写大段 JSON。
    /// </summary>
    public required JsonObject InputSchema { get; init; }
}

/// <summary>
/// 工具执行结果。
///
/// MCP 规范里 tools/call 的结果是 content 数组（内容块），常见块类型为 text。
/// 本服务统一返回一个 text 块，并额外携带结构化数据（structuredContent），
/// 方便模型读文本、程序读字段。
/// </summary>
public sealed class McpToolResult
{
    /// <summary>结果文本。失败时是错误说明，成功时是给模型阅读的正文。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>是否为错误结果（客户端会把 isError 标红展示）。</summary>
    public bool IsError { get; init; }

    /// <summary>可选的附加元数据（如调用耗时、条目数量），不参与模型上下文。</summary>
    public JsonObject? Meta { get; init; }

    /// <summary>快捷构造一个成功结果。</summary>
    public static McpToolResult Ok(string text, JsonObject? meta = null) =>
        new() { Text = text, IsError = false, Meta = meta };

    /// <summary>快捷构造一个失败结果。</summary>
    public static McpToolResult Fail(string text) =>
        new() { Text = text, IsError = true };
}

/// <summary>
/// 所有 MCP 工具都要实现的契约。
///
/// 实现类会被 Program.cs 中的反射扫描自动注册，无需手工登记：
/// 只要在 Mcp/Tools 目录下新建一个 sealed class 并实现本接口即可。
/// </summary>
public interface IMcpTool
{
    /// <summary>工具定义（名称、说明、参数 Schema）。每次访问都应返回同一份定义。</summary>
    McpToolDefinition Definition { get; }

    /// <summary>
    /// 执行工具。
    /// </summary>
    /// <param name="arguments">
    /// 客户端传入的参数对象（可能为空对象）；字段是否存在由各工具自行校验，
    /// 推荐使用 <see cref="ToolArgs"/> 提供的读取方法获得带默认值和中文报错的体验。
    /// </param>
    /// <param name="cancellationToken">取消令牌，长耗时操作（如抓取网页）应传递它。</param>
    /// <returns>执行结果；参数不合法时可抛出 <see cref="ToolException"/>，由上层转换为 isError 结果。</returns>
    Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken);
}
