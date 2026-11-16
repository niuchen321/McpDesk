using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：生成随机内容（UUID、密码、随机数、掷骰子）。
///
/// 全部使用 <see cref="RandomNumberGenerator"/>（密码学安全随机源），
/// 而不是 Random，避免生成的密码/令牌可被预测。
/// </summary>
public sealed class RandomGenerateTool : IMcpTool
{
    /// <summary>密码字符集：去掉了容易混淆的 0/O、1/l/I。</summary>
    private const string PasswordAlphabet =
        "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>额外的符号字符（可选）。</summary>
    private const string PasswordSymbols = "!@#$%^&*()-_=+[]{};:,.?";

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "random_generate",
        Title = "生成随机内容",
        Description =
            "生成 UUID、随机密码、指定范围的随机整数，或模拟掷骰子。" +
            "kind 参数决定生成什么：uuid / password / number / dice。",
        InputSchema = new JsonSchemaBuilder()
            .AddEnum(
                "kind",
                "生成类型：uuid（UUID v4）、password（随机密码）、number（区间随机整数）、dice（掷骰子）。",
                new[] { "uuid", "password", "number", "dice" },
                required: true)
            .AddInteger("count", "生成数量（1～20），默认 1。", defaultValue: 1, minimum: 1, maximum: 20)
            .AddInteger("length", "password 的密码长度（6～128），默认 16。", defaultValue: 16, minimum: 6, maximum: 128)
            .AddBoolean("includeSymbols", "password 是否包含符号字符，默认 true。", defaultValue: true)
            .AddInteger("min", "number 的最小值（含），默认 1。", defaultValue: 1)
            .AddInteger("max", "number 的最大值（含），默认 100。", defaultValue: 100)
            .AddInteger("sides", "dice 的骰子面数（2～1000），默认 6。", defaultValue: 6, minimum: 2, maximum: 1000)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var kind = args.RequiredString("kind", "生成类型").Trim().ToLowerInvariant();
        var count = args.OptionalInt("count", 1, 1, 20) ?? 1;

        var values = new List<string>();
        var meta = new JsonObject { ["kind"] = kind };

        switch (kind)
        {
            case "uuid":
                for (var i = 0; i < count; i++)
                {
                    values.Add(Guid.NewGuid().ToString());
                }

                break;

            case "password":
            {
                var length = args.OptionalInt("length", 16, 6, 128) ?? 16;
                var includeSymbols = args.OptionalBool("includeSymbols", true);
                if (includeSymbols)
                {
                    for (var i = 0; i < count; i++)
                    {
                        values.Add(GeneratePassword(length, PasswordAlphabet + PasswordSymbols));
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        values.Add(GeneratePassword(length, PasswordAlphabet));
                    }
                }

                meta["length"] = length;
                break;
            }

            case "number":
            {
                var min = args.OptionalInt("min", 1) ?? 1;
                var max = args.OptionalInt("max", 100) ?? 100;
                if (min > max)
                {
                    throw new ToolException($"参数范围不合法：min（{min}）不能大于 max（{max}）。");
                }

                for (var i = 0; i < count; i++)
                {
                    // RandomNumberGenerator.GetInt32 的上界是「不含」，所以 max 要 +1
                    values.Add(RandomNumberGenerator.GetInt32(min, max + 1).ToString());
                }

                meta["min"] = min;
                meta["max"] = max;
                break;
            }

            case "dice":
            {
                var sides = args.OptionalInt("sides", 6, 2, 1000) ?? 6;
                var total = 0;
                for (var i = 0; i < count; i++)
                {
                    var roll = RandomNumberGenerator.GetInt32(1, sides + 1);
                    total += roll;
                    values.Add(roll.ToString());
                }

                meta["sides"] = sides;
                meta["total"] = total;
                break;
            }

            default:
                throw new ToolException($"不支持的生成类型「{kind}」，可选：uuid、password、number、dice。");
        }

        var text = count == 1
            ? values[0]
            : string.Join("\n", values.Select((v, i) => $"{i + 1}. {v}"));

        if (kind == "dice" && count > 1)
        {
            text += $"\n合计：{meta["total"]}";
        }

        return Task.FromResult(McpToolResult.Ok(text, meta));
    }

    /// <summary>
    /// 生成随机密码。
    /// 先按字符集均匀取字符，再保证至少包含一个字母与一个数字（含符号时再加一个符号），
    /// 避免生成出「全是数字」这类弱密码。
    /// </summary>
    private static string GeneratePassword(int length, string alphabet)
    {
        var buffer = new char[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        // 保证组成多样性：随机替换前几位
        if (length >= 3)
        {
            buffer[0] = (char)('a' + RandomNumberGenerator.GetInt32(26));
            buffer[1] = (char)('0' + RandomNumberGenerator.GetInt32(10));
            if (alphabet.Contains('!') && length >= 4)
            {
                buffer[2] = PasswordSymbols[RandomNumberGenerator.GetInt32(PasswordSymbols.Length)];
            }
        }

        // 打乱顺序，避免固定位置规律
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        return new string(buffer);
    }
}
