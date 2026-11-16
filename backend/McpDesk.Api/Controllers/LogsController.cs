using McpDesk.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpDesk.Api.Controllers;

/// <summary>
/// 调用日志接口（供 Vue 管理后台的「调用日志」面板使用）。
///
/// 日志只保存在内存中，进程重启即清空；这是刻意设计：
/// 个人工具不需要额外的数据库，也避免把调用参数长期落盘。
/// </summary>
[ApiController]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly CallLogStore _logs;

    /// <summary>构造函数：注入日志存储。</summary>
    public LogsController(CallLogStore logs)
    {
        _logs = logs;
    }

    /// <summary>获取最近的调用记录（最新的在前）。</summary>
    [HttpGet]
    public IActionResult Recent([FromQuery] int limit = 50)
    {
        var entries = _logs.Recent(limit);
        return Ok(new { count = entries.Count, logs = entries });
    }

    /// <summary>清空全部调用记录。</summary>
    [HttpDelete]
    public IActionResult Clear()
    {
        _logs.Clear();
        return NoContent();
    }
}
