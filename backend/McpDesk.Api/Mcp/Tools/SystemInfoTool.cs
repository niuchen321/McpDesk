using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：获取本机系统信息。
///
/// 全部为只读操作，用于回答「这台机器什么配置」「磁盘还剩多少」这类问题。
/// 不返回任何敏感信息（不含 MAC 地址、不含用户目录外的路径）。
/// </summary>
public sealed class SystemInfoTool : IMcpTool
{
    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "system_info",
        Title = "系统信息",
        Description =
            "返回运行本 MCP 服务所在电脑的基本信息：操作系统、架构、CPU 核心数、" +
            "内存与进程占用、各磁盘容量、.NET 运行时版本。只读，无副作用。",
        InputSchema = new JsonSchemaBuilder()
            .AddBoolean("includeDrives", "是否包含磁盘容量信息，默认 true。", defaultValue: true)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var includeDrives = args.OptionalBool("includeDrives", true);

        var process = System.Diagnostics.Process.GetCurrentProcess();
        var memoryInfo = GC.GetGCMemoryInfo();

        var builder = new StringBuilder();
        builder.AppendLine("### 系统");
        builder.AppendLine($"- 操作系统：{RuntimeInformation.OSDescription}");
        builder.AppendLine($"- 系统架构：{RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"- 计算机名：{Environment.MachineName}");
        builder.AppendLine($"- 当前用户：{Environment.UserName}");
        builder.AppendLine($"- CPU 逻辑核心：{Environment.ProcessorCount}");
        builder.AppendLine();
        builder.AppendLine("### 运行时");
        builder.AppendLine($"- .NET 版本：{Environment.Version}");
        builder.AppendLine($"- 进程内存：{process.WorkingSet64 / 1024.0 / 1024.0:F1} MB");
        builder.AppendLine($"- 系统可用内存（GC 视角）：{memoryInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0:F1} GB");
        builder.AppendLine($"- 进程已运行：{TimeSpan.FromMilliseconds(Environment.TickCount64):hh\\:mm\\:ss}");

        var meta = new JsonObject
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["machineName"] = Environment.MachineName,
            ["processorCount"] = Environment.ProcessorCount,
            ["dotnetVersion"] = Environment.Version.ToString(),
            ["processMemoryMb"] = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
        };

        if (includeDrives)
        {
            builder.AppendLine();
            builder.AppendLine("### 磁盘");

            var drives = new JsonArray();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady).OrderBy(d => d.Name))
            {
                try
                {
                    var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                    var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    var usedPercent = totalGb > 0 ? (totalGb - freeGb) / totalGb * 100 : 0;

                    builder.AppendLine(
                        $"- {drive.Name}（{drive.DriveFormat}）：可用 {freeGb:F1} GB / 共 {totalGb:F1} GB（已用 {usedPercent:F0}%）");

                    drives.Add(new JsonObject
                    {
                        ["name"] = drive.Name,
                        ["format"] = drive.DriveFormat,
                        ["totalGb"] = Math.Round(totalGb, 1),
                        ["freeGb"] = Math.Round(freeGb, 1),
                    });
                }
                catch (IOException)
                {
                    // 某些可移动磁盘在枚举后立刻被拔出，忽略即可
                }
            }

            meta["drives"] = drives;
        }

        return Task.FromResult(McpToolResult.Ok(builder.ToString().TrimEnd(), meta));
    }
}
