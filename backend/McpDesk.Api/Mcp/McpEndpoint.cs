using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using McpDesk.Api.Services;
using Microsoft.Extensions.Options;

namespace McpDesk.Api.Mcp;

/// <summary>
/// MCP 的 HTTP 传输层实现（Streamable HTTP）：负责鉴权、解析请求体、
/// 支持单条与批量 JSON-RPC 消息，并把结果写回客户端。
///
/// 与 <see cref="McpRequestHandler"/> 的分工：
///   · 本类只关心 HTTP 细节（状态码、请求头、序列化）；
///   · 协议语义（initialize / tools/call ...）全部交给 McpRequestHandler。
/// </summary>
public static class McpEndpoint
{
    /// <summary>序列化响应时使用的选项：保留中文原文、不缩进（节省带宽）。</summary>
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>处理 POST /mcp。</summary>
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        McpRequestHandler handler,
        IOptions<AppOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("McpDesk.McpEndpoint");

        // ---------------------------------------------------------------
        // 1. 鉴权：配置了 ApiKey 时要求请求头一致，否则拒绝。
        //    本机自用一般留空；暴露到局域网时建议在 appsettings 中设置。
        // ---------------------------------------------------------------
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var provided = context.Request.Headers["X-Api-Key"].ToString();
            if (!string.Equals(provided, apiKey, StringComparison.Ordinal))
            {
                logger.LogWarning("MCP 请求被拒绝：ApiKey 不匹配，来源 {Remote}", context.Connection.RemoteIpAddress);
                return Results.Json(new
                {
                    jsonrpc = McpProtocol.JsonRpcVersion,
                    id = (object?)null,
                    error = new { code = -32001, message = "未授权：请求头 X-Api-Key 缺失或不正确。" },
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        // ---------------------------------------------------------------
        // 2. 会话标识：MCP 规范允许服务端通过 Mcp-Session-Id 头维持会话。
        //    本服务无状态，但仍按规范回写一个 ID，方便客户端与日志关联。
        // ---------------------------------------------------------------
        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers["Mcp-Session-Id"] = sessionId;

        // ---------------------------------------------------------------
        // 3. 解析请求体
        //    先把正文读成字节数组并校验 UTF-8：
        //    · 有些客户端（例如 Windows 控制台里的 curl）会用本地代码页发送中文，
        //      这会得到非 UTF-8 的字节，直接交给 STJ 会在后续取值时才抛出难以理解的异常；
        //    · 提前校验可以返回清晰的 -32700 解析错误；
        //    · 同时限制正文大小，避免恶意/误发的大请求占满内存。
        // ---------------------------------------------------------------
        const int MaxBodyBytes = 4 * 1024 * 1024;

        var bodyBytes = await ReadBodyAsync(context.Request.Body, MaxBodyBytes, cancellationToken);
        if (bodyBytes is null)
        {
            return Results.Text(
                BuildError(null, McpErrorCodes.InvalidRequest, "请求体过大（超过 4 MB）。").ToJsonString(ResponseJson),
                "application/json; charset=utf-8",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!Utf8.IsValid(bodyBytes))
        {
            return JsonError(null, McpErrorCodes.ParseError,
                "请求体不是合法的 UTF-8 文本，请确认客户端以 UTF-8 编码发送 JSON。");
        }

        JsonNode? body;
        try
        {
            body = JsonNode.Parse(bodyBytes);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "请求体不是合法 JSON");
            return JsonError(null, McpErrorCodes.ParseError, "请求体不是合法的 JSON。");
        }

        if (body is null)
        {
            return JsonError(null, McpErrorCodes.InvalidRequest, "请求体为空。");
        }

        // ---------------------------------------------------------------
        // 4. 分发：单条消息 或 批量数组（JSON-RPC 2.0 batch）
        // ---------------------------------------------------------------
        if (body is JsonArray batch)
        {
            if (batch.Count == 0)
            {
                return JsonError(null, McpErrorCodes.InvalidRequest, "批量请求不能为空数组。");
            }

            var responses = new JsonArray();

            foreach (var item in batch)
            {
                if (item is not JsonObject single)
                {
                    responses.Add(BuildError(null, McpErrorCodes.InvalidRequest, "批量请求中的每一项都必须是对象。"));
                    continue;
                }

                var response = await handler.HandleAsync(single, cancellationToken);
                if (response is not null)
                {
                    responses.Add(response);
                }
            }

            // 全部是通知（没有响应）时按规范返回 202
            return responses.Count == 0
                ? Results.StatusCode(StatusCodes.Status202Accepted)
                : Results.Text(responses.ToJsonString(ResponseJson), "application/json; charset=utf-8");
        }

        if (body is not JsonObject request)
        {
            return JsonError(null, McpErrorCodes.InvalidRequest, "请求必须是 JSON 对象或对象数组。");
        }

        // 校验 jsonrpc 字段（宽松处理：缺失也接受，兼容部分早期客户端）
        var jsonRpc = request["jsonrpc"]?.GetValue<string>();
        if (jsonRpc is not null && jsonRpc != McpProtocol.JsonRpcVersion)
        {
            return JsonError(request["id"]?.DeepClone(), McpErrorCodes.InvalidRequest, "不支持的 jsonrpc 版本，应为 \"2.0\"。");
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        // 通知类消息（没有 id）不需要响应体
        if (result is null)
        {
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        return Results.Text(result.ToJsonString(ResponseJson), "application/json; charset=utf-8");
    }

    /// <summary>
    /// 把请求体完整读入内存，超过 maxBytes 时返回 null。
    /// 用分块读取而不是 CopyTo，是为了能在超限时立刻停止，而不是先读完再判断。
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(Stream input, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await input.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>构造并返回一条 JSON-RPC 错误响应（HTTP 200，符合 JSON-RPC 习惯）。</summary>
    private static IResult JsonError(JsonNode? id, int code, string message)
    {
        var payload = BuildError(id, code, message);
        return Results.Text(payload.ToJsonString(ResponseJson), "application/json; charset=utf-8");
    }

    /// <summary>只构造错误对象，供批量分支复用。</summary>
    private static JsonObject BuildError(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = McpProtocol.JsonRpcVersion,
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
}
