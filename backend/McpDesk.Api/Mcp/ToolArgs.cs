using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp;

/// <summary>
/// 工具执行过程中「用户侧错误」的异常类型（例如缺少必填参数、参数越界、笔记不存在）。
///
/// 之所以单独定义而不直接抛 Exception：
///   · MCP 规范要求区分「工具执行失败」与「服务端崩溃」，前者应返回 isError 文本；
///   · 上层只捕获这个类型并原样展示中文提示，其它异常一律按内部错误处理并记录日志。
/// </summary>
public sealed class ToolException : Exception
{
    /// <summary>用中文错误信息构造异常。</summary>
    public ToolException(string message) : base(message)
    {
    }
}

/// <summary>
/// 工具参数读取器：把 <see cref="JsonObject"/> 包装成带类型转换与中文报错的读取接口。
///
/// 设计目标：
///   · 参数缺失、类型不对时抛出 <see cref="ToolException"/>，错误信息直接面向最终用户；
///   · 支持默认值，让「可选参数」在工具代码里不需要到处写 ?? 判断；
///   · 同时接受 JSON 数字与数字字符串（很多客户端会把数值序列化成字符串）。
/// </summary>
public readonly struct ToolArgs
{
    private readonly JsonObject _args;

    /// <summary>包装一个参数对象；客户端未传参数时应传入空对象。</summary>
    public ToolArgs(JsonObject args)
    {
        _args = args;
    }

    /// <summary>参数是否包含指定字段（且值不为 null）。</summary>
    public bool Has(string name) => _args.TryGetPropertyValue(name, out var node) && node is not null;

    /// <summary>读取必填字符串。缺失或为空白时抛出中文错误。</summary>
    public string RequiredString(string name, string label)
    {
        var value = OptionalString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ToolException($"缺少必填参数「{label}」（{name}）。");
        }

        return value;
    }

    /// <summary>读取可选字符串；缺失时返回 <paramref name="defaultValue"/>。</summary>
    public string? OptionalString(string name, string? defaultValue = null)
    {
        if (!Has(name))
        {
            return defaultValue;
        }

        var node = _args[name];
        if (node is JsonValue value)
        {
            // 字符串直接取；数字/布尔转成字符串，兼容不严谨的客户端
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (value.TryGetValue<JsonElement>(out var element))
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => defaultValue,
                };
            }
        }

        return defaultValue;
    }

    /// <summary>读取必填整数。缺失、非数字或越界时抛出中文错误。</summary>
    public int RequiredInt(string name, string label, int min = int.MinValue, int max = int.MaxValue)
    {
        var value = OptionalInt(name, null, min, max);
        if (!value.HasValue)
        {
            throw new ToolException($"缺少必填参数「{label}」（{name}），且必须是整数。");
        }

        return value.Value;
    }

    /// <summary>读取可选整数；缺失时返回 <paramref name="defaultValue"/>。</summary>
    public int? OptionalInt(
        string name,
        int? defaultValue = null,
        int min = int.MinValue,
        int max = int.MaxValue)
    {
        if (!Has(name))
        {
            return defaultValue;
        }

        var parsed = ParseDouble(_args[name]);
        if (parsed is null)
        {
            throw new ToolException($"参数「{name}」必须是数字。");
        }

        var rounded = (int)Math.Round(parsed.Value, MidpointRounding.AwayFromZero);
        if (rounded < min || rounded > max)
        {
            throw new ToolException($"参数「{name}」需要在 {min} 到 {max} 之间，当前为 {rounded}。");
        }

        return rounded;
    }

    /// <summary>读取可选布尔值；缺失时返回 <paramref name="defaultValue"/>。</summary>
    public bool OptionalBool(string name, bool defaultValue = false)
    {
        if (!Has(name))
        {
            return defaultValue;
        }

        var node = _args[name];
        if (node is JsonValue value && value.TryGetValue<bool>(out var result))
        {
            return result;
        }

        // 兼容 "true"/"false" 字符串形式
        var text = OptionalString(name);
        return text is not null && text.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>读取字符串数组（标签列表）。缺失时返回空列表。</summary>
    public IReadOnlyList<string> OptionalStringList(string name)
    {
        var result = new List<string>();
        if (!Has(name))
        {
            return result;
        }

        if (_args[name] is not JsonArray array)
        {
            throw new ToolException($"参数「{name}」必须是字符串数组。");
        }

        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
        }

        return result;
    }

    /// <summary>读取原始 JSON 节点（用于需要自定义解析的场景）。</summary>
    public JsonNode? Raw(string name) => Has(name) ? _args[name] : null;

    /// <summary>把 JSON 数字或数字字符串统一解析为 double。</summary>
    private static double? ParseDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<JsonElement>(out var element))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble();
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
