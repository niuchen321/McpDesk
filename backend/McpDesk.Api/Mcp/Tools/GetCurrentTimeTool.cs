using System.Globalization;
using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：获取当前时间。
///
/// 模型本身没有「现在几点」的概念，这个工具是最基础也最常用的一个。
/// 支持指定时区（IANA 名称如 Asia/Shanghai，也兼容 Windows 时区 ID 如 China Standard Time）。
/// </summary>
public sealed class GetCurrentTimeTool : IMcpTool
{
    /// <summary>工具定义：名称、说明与参数 Schema。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "get_current_time",
        Title = "获取当前时间",
        Description =
            "返回当前的日期与时间（含时区、星期、Unix 时间戳）。" +
            "默认使用服务器本地时区，也可以通过 timezone 参数指定，例如 Asia/Shanghai、America/New_York。",
        InputSchema = new JsonSchemaBuilder()
            .AddString(
                "timezone",
                "时区标识。留空表示服务器本地时区；可填 IANA 名称（Asia/Shanghai）或 Windows 时区 ID（China Standard Time）。",
                format: null)
            .AddBoolean("includeUnix", "是否同时返回 Unix 秒级时间戳。", defaultValue: true)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var timezoneId = args.OptionalString("timezone");
        var includeUnix = args.OptionalBool("includeUnix", true);

        var zone = ResolveTimeZone(timezoneId);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);

        var weekDay = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日",
        };

        // 给模型一份人类可读的时间，同时把结构化字段放进 meta，便于程序使用
        var text =
            $"当前时间：{now:yyyy-MM-dd HH:mm:ss}（{weekDay}）\n" +
            $"时区：{zone.Id}，UTC{FormatOffset(now.Offset)}";

        var meta = new JsonObject
        {
            ["iso"] = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["timezone"] = zone.Id,
            ["utcOffsetMinutes"] = (int)now.Offset.TotalMinutes,
            ["weekday"] = weekDay,
        };

        if (includeUnix)
        {
            var unix = now.ToUnixTimeSeconds();
            text += $"\nUnix 时间戳：{unix}";
            meta["unix"] = unix;
        }

        return Task.FromResult(McpToolResult.Ok(text, meta));
    }

    /// <summary>把字符串解析为 TimeZoneInfo；为空返回本地时区。</summary>
    private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId) ||
            timezoneId.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            // .NET 6+ 在 Windows 上同时支持 IANA 与 Windows 时区 ID
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ToolException(
                $"找不到时区「{timezoneId}」。请使用 IANA 名称，例如 Asia/Shanghai、Europe/London、America/New_York。");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ToolException($"时区数据无效：「{timezoneId}」。");
        }
    }

    /// <summary>把偏移量格式化为 +08:00 这样的形式。</summary>
    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:00}:{abs.Minutes:00}";
    }
}
