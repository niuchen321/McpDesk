using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：文本处理（编码转换、哈希、JSON 格式化、大小写等）。
///
/// 把高频的文本小工具集中到一个 operation 枚举里，避免为每个小功能单独开一个工具，
/// 既减少模型选择成本，也便于前端下拉框展示。
/// </summary>
public sealed class TextTransformTool : IMcpTool
{
    /// <summary>支持的文本处理操作。</summary>
    private static readonly string[] Operations =
    {
        "base64_encode",
        "base64_decode",
        "url_encode",
        "url_decode",
        "sha256",
        "md5",
        "sha1",
        "json_format",
        "json_minify",
        "upper",
        "lower",
        "reverse",
        "count",
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "text_transform",
        Title = "文本处理",
        Description =
            "对一段文本执行常见操作：Base64 编解码、URL 编解码、SHA256/MD5/SHA1 哈希、" +
            "JSON 格式化或压缩、大小写转换、字符反转、字符统计。" +
            "operation 参数决定具体执行哪种操作。",
        InputSchema = new JsonSchemaBuilder()
            .AddEnum(
                "operation",
                "要执行的操作。" +
                "base64_encode/base64_decode：Base64 编解码；" +
                "url_encode/url_decode：URL 转义与还原；" +
                "sha256/md5/sha1：计算哈希（返回小写十六进制）；" +
                "json_format/json_minify：JSON 美化或压缩；" +
                "upper/lower/reverse：全大写、全小写、字符反转；" +
                "count：统计字符数与行数。",
                Operations,
                required: true)
            .AddString("text", "要处理的文本。json_format / json_minify 时必须是合法 JSON 字符串。", required: true, maxLength: 100000)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var operation = args.RequiredString("operation", "操作").Trim().ToLowerInvariant();
        var text = args.Raw("text") is JsonValue value && value.TryGetValue<string>(out var raw) ? raw : string.Empty;

        if (!Operations.Contains(operation))
        {
            throw new ToolException($"不支持的操作「{operation}」。可选值：{string.Join("、", Operations)}。");
        }

        if (text.Length == 0 && operation is not "count")
        {
            throw new ToolException("参数 text 不能为空。");
        }

        var result = operation switch
        {
            // Base64：UTF-8 编码
            "base64_encode" => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            "base64_decode" => DecodeBase64(text),

            // URL：Uri.EscapeDataString 与 WebUtility 均可；这里用 UrlEncoder 保证与网页一致
            "url_encode" => Uri.EscapeDataString(text),
            "url_decode" => Uri.UnescapeDataString(text),

            // 哈希：统一小写十六进制
            "sha256" => HashHex(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            "md5" => HashHex(MD5.HashData(Encoding.UTF8.GetBytes(text))),
            "sha1" => HashHex(SHA1.HashData(Encoding.UTF8.GetBytes(text))),

            // JSON
            "json_format" => FormatJson(text, indented: true),
            "json_minify" => FormatJson(text, indented: false),

            // 字符串
            "upper" => text.ToUpperInvariant(),
            "lower" => text.ToLowerInvariant(),
            "reverse" => ReverseText(text),
            "count" => CountText(text),

            _ => throw new ToolException($"不支持的操作：{operation}"),
        };

        var meta = new JsonObject
        {
            ["operation"] = operation,
            ["inputLength"] = text.Length,
            ["outputLength"] = result.Length,
        };

        return Task.FromResult(McpToolResult.Ok(result, meta));
    }

    /// <summary>Base64 解码：给出更友好的错误提示。</summary>
    private static string DecodeBase64(string text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            var decoded = Encoding.UTF8.GetString(bytes);

            // 若解码结果包含大量替换字符，说明原文可能不是 UTF-8 文本
            if (decoded.Contains('�'))
            {
                return Convert.ToHexString(bytes).ToLowerInvariant() +
                       "\n（提示：解码后的字节不是有效的 UTF-8 文本，上面是十六进制表示）";
            }

            return decoded;
        }
        catch (FormatException)
        {
            throw new ToolException("Base64 解码失败：输入不是合法的 Base64 字符串。");
        }
    }

    /// <summary>把哈希字节转成小写十六进制字符串。</summary>
    private static string HashHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>格式化或压缩 JSON。</summary>
    private static string FormatJson(string text, bool indented)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ToolException($"JSON 解析失败：{ex.Message}");
        }

        if (node is null)
        {
            throw new ToolException("JSON 内容为空。");
        }

        var options = indented
            ? PrettyJson
            : new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

        return node.ToJsonString(options);
    }

    /// <summary>反转字符串。注意：按 UTF-16 码元反转，emoji 等代理对可能被拆开，这里做了保护。</summary>
    private static string ReverseText(string text)
    {
        var elements = new List<string>(text.Length);
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        elements.Reverse();
        return string.Concat(elements);
    }

    /// <summary>统计字符数、行数、行数（不含空行）与词数（按空白切分）。</summary>
    private static string CountText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var nonEmptyLines = lines.Count(l => l.Trim().Length > 0);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return $"字符数：{text.Length}\n" +
               $"行数：{lines.Length}\n" +
               $"非空行：{nonEmptyLines}\n" +
               $"词数（按空白切分）：{words}";
    }
}
