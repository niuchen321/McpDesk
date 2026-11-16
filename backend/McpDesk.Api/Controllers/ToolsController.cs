using System.Diagnostics;
using System.Text.Json.Nodes;
using McpDesk.Api.Mcp;
using McpDesk.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpDesk.Api.Controllers;

/// <summary>
/// 工具相关的 REST 接口（供 Vue 管理后台使用）。
///
/// 与 MCP 端点的区别：
///   · MCP 端点（POST /mcp）面向 Claude Desktop / Cursor 这类 AI 客户端，使用 JSON-RPC；
///   · 这里的 /api/tools 面向人，使用普通 JSON，方便前端直接渲染表单和结果。
///
/// 两者最终都调用同一个 <see cref="IMcpTool.InvokeAsync"/>，保证行为完全一致。
/// 区别只是调用日志的来源标记为 "api"。
/// </summary>
[ApiController]
[Route("api/tools")]
public sealed class ToolsController : ControllerBase
{
    private readonly McpToolRegistry _registry;
    private readonly CallLogStore _logs;

    /// <summary>构造函数：注入工具注册表与日志存储。</summary>
    public ToolsController(McpToolRegistry registry, CallLogStore logs)
    {
        _registry = registry;
        _logs = logs;
    }

    /// <summary>
    /// 获取全部工具的定义（名称、标题、说明、输入 JSON Schema）。
    /// 前端根据 inputSchema 自动生成调用表单。
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var tools = _registry.All
            .Select(tool => new
            {
                name = tool.Definition.Name,
                title = tool.Definition.Title ?? tool.Definition.Name,
                description = tool.Definition.Description,
                inputSchema = tool.Definition.InputSchema,
            })
            .ToList();

        return Ok(new { count = tools.Count, tools });
    }

    /// <summary>
    /// 获取单个工具的完整定义。
    /// </summary>
    [HttpGet("{name}")]
    public IActionResult Get(string name)
    {
        var tool = _registry.Find(name);
        if (tool is null)
        {
            return NotFound(new { error = $"不存在名为「{name}」的工具。" });
        }

        return Ok(new
        {
            name = tool.Definition.Name,
            title = tool.Definition.Title ?? tool.Definition.Name,
            description = tool.Definition.Description,
            inputSchema = tool.Definition.InputSchema,
        });
    }

    /// <summary>
    /// 调用工具。
    ///
    /// 请求体就是工具的 arguments 对象，例如：
    ///   { "expression": "1+2*3", "precision": 4 }
    /// 无参数的工具可以传空对象 {}。
    /// </summary>
    [HttpPost("{name}/invoke")]
    public async Task<IActionResult> Invoke(
        string name,
        [FromBody] JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        var tool = _registry.Find(name);
        if (tool is null)
        {
            return NotFound(new { error = $"不存在名为「{name}」的工具。" });
        }

        arguments ??= new JsonObject();

        // 与 MCP 端点使用同一套执行/容错逻辑
        var stopwatch = Stopwatch.StartNew();
        McpToolResult result;
        try
        {
            result = await McpRequestHandler.ExecuteToolAsync(tool, arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            // 未预料的异常：记录日志并给前端返回 500，避免把堆栈暴露给页面
            stopwatch.Stop();
            _logs.Add(name, arguments, true, ex.Message, stopwatch.Elapsed.TotalMilliseconds, "api");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "工具执行时发生内部错误，请查看服务端日志。" });
        }

        stopwatch.Stop();
        _logs.Add(name, arguments, result.IsError, result.Text, stopwatch.Elapsed.TotalMilliseconds, "api");

        return Ok(new
        {
            tool = name,
            isError = result.IsError,
            text = result.Text,
            meta = result.Meta,
            elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
        });
    }
}
