using System.Diagnostics;
using System.Runtime.InteropServices;
using McpDesk.Api.Mcp;
using McpDesk.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace McpDesk.Api.Controllers;

/// <summary>
/// 运行状态接口（供 Vue 管理后台顶部的「服务状态」卡片使用）。
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly AppOptions _options;
    private readonly NoteStore _notes;
    private readonly FileSandbox _sandbox;
    private readonly McpToolRegistry _registry;

    /// <summary>构造函数：注入状态展示所需的各项服务。</summary>
    public SystemController(
        IOptions<AppOptions> options,
        NoteStore notes,
        FileSandbox sandbox,
        McpToolRegistry registry)
    {
        _options = options.Value;
        _notes = notes;
        _sandbox = sandbox;
        _registry = registry;
    }

    /// <summary>
    /// 返回服务状态概览：版本、协议版本、工具数量、数据路径、沙盒目录、内存占用等。
    /// 注意：这里不会返回 ApiKey 等敏感配置。
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        var process = Process.GetCurrentProcess();

        // 用进程启动时间计算运行时长，比记录「控制器首次实例化时间」更准确
        var startedAt = new DateTimeOffset(process.StartTime);
        var uptime = DateTimeOffset.Now - startedAt;

        return Ok(new
        {
            server = AppInfo.Name,
            title = AppInfo.Title,
            version = AppInfo.Version,
            description = AppInfo.Description,

            // MCP 协议信息
            protocolVersion = McpProtocol.Version,
            supportedProtocolVersions = McpProtocol.SupportedVersions,
            mcpEndpoint = "/mcp",

            // 运行环境
            machineName = Environment.MachineName,
            osDescription = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            processId = Environment.ProcessId,
            startedAt,
            uptimeSeconds = Math.Round(uptime.TotalSeconds, 0),

            // 资源占用
            workingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
            processorCount = Environment.ProcessorCount,

            // 业务数据
            toolCount = _registry.All.Count,
            noteCount = _notes.GetAll().Count,

            // 配置（只暴露可读信息）
            dataFile = _notes.FilePath,
            sandboxRoot = _sandbox.Root,
            apiKeyRequired = !string.IsNullOrWhiteSpace(_options.ApiKey),
            maxFetchBytes = _options.MaxFetchBytes,
            maxLogEntries = _options.MaxLogEntries,
            allowPrivateNetwork = _options.AllowPrivateNetwork,
        });
    }
}
