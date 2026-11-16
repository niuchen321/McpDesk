using System.Diagnostics;
using System.Text.Json.Nodes;
using McpDesk.Api.Models;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp;

/// <summary>
/// MCP 请求处理器：把一条 JSON-RPC 请求分发到对应的能力实现。
///
/// 本类实现了 MCP 规范中最常用的三块能力：
///   · tools      —— 工具列表与调用（本项目的核心）
///   · resources  —— 把个人笔记以只读资源的形式暴露给客户端
///   · prompts    —— 预置提示词模板，方便客户端一键使用
///
/// 约定：对于 notifications/* 这类「不需要响应」的消息，返回 null；
/// 其余情况一律返回一个合法的 JSON-RPC 响应对象。
/// </summary>
public sealed class McpRequestHandler
{
    private readonly McpToolRegistry _registry;
    private readonly NoteStore _notes;
    private readonly CallLogStore _logs;
    private readonly ILogger<McpRequestHandler> _logger;

    /// <summary>资源 URI 前缀：MCP 要求资源有唯一 URI，这里用自定义 scheme。</summary>
    private const string NoteUriPrefix = "mcpdesk://notes/";

    public McpRequestHandler(
        McpToolRegistry registry,
        NoteStore notes,
        CallLogStore logs,
        ILogger<McpRequestHandler> logger)
    {
        _registry = registry;
        _notes = notes;
        _logs = logs;
        _logger = logger;
    }

    /// <summary>
    /// 处理一条 JSON-RPC 请求。
    /// </summary>
    /// <param name="request">已解析的请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应对象；通知类消息返回 null。</returns>
    public async Task<JsonObject?> HandleAsync(JsonObject request, CancellationToken cancellationToken)
    {
        // 取出 id 并深拷贝：响应需要原样带回 id（可能是字符串、数字或 null）
        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(method))
        {
            return Error(id, McpErrorCodes.InvalidRequest, "请求缺少 method 字段。");
        }

        var parameters = request["params"] as JsonObject ?? new JsonObject();

        try
        {
            return method switch
            {
                // ---------------- 生命周期 ----------------
                "initialize" => Success(id, BuildInitializeResult(parameters)),

                // 客户端在 initialize 之后会发送该通知，表示握手完成。
                // 通知没有 id，不需要响应。
                "notifications/initialized" => null,

                // ---------------- 通用 ----------------
                "ping" => Success(id, new JsonObject()),

                // 部分客户端会同步日志级别；本服务直接接受即可。
                "logging/setLevel" => Success(id, new JsonObject()),

                // ---------------- 工具 ----------------
                "tools/list" => Success(id, new JsonObject
                {
                    ["tools"] = _registry.BuildToolsJsonArray(),
                }),
                "tools/call" => await CallToolAsync(id, parameters, cancellationToken),

                // ---------------- 资源（笔记） ----------------
                "resources/list" => Success(id, BuildResourcesList()),
                "resources/read" => Success(id, BuildResourceRead(parameters)),

                // ---------------- 提示词 ----------------
                "prompts/list" => Success(id, BuildPromptsList()),
                "prompts/get" => Success(id, BuildPromptGet(parameters)),

                // ---------------- 未知方法 ----------------
                _ => Error(id, McpErrorCodes.MethodNotFound, $"不支持的方法：{method}"),
            };
        }
        catch (ToolException ex)
        {
            // 参数类错误直接作为 InvalidParams 返回，便于客户端提示用户
            return Error(id, McpErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 MCP 请求失败：{Method}", method);
            return Error(id, McpErrorCodes.InternalError, "服务端处理请求时发生内部错误，请查看服务端日志。");
        }
    }

    /// <summary>
    /// 处理 initialize：返回协议版本、能力声明与服务信息。
    /// </summary>
    private static JsonObject BuildInitializeResult(JsonObject parameters)
    {
        // 客户端会告知它支持的协议版本；如果不在我们的支持列表里，返回服务端版本让客户端决定
        var clientVersion = parameters["protocolVersion"]?.GetValue<string>();
        var negotiated = clientVersion is not null && McpProtocol.SupportedVersions.Contains(clientVersion)
            ? clientVersion
            : McpProtocol.Version;

        return new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject
            {
                // 工具能力：listChanged=false 表示本服务的工具列表在运行期不会变化
                ["tools"] = new JsonObject { ["listChanged"] = false },
                // 资源能力：笔记是只读的，不支持订阅变更
                ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
                // 提示词能力：内置模板，运行期不变
                ["prompts"] = new JsonObject { ["listChanged"] = false },
                // 日志能力：本服务接受 setLevel，但不主动推送日志消息
                ["logging"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = AppInfo.Name,
                ["title"] = AppInfo.Title,
                ["version"] = AppInfo.Version,
                ["description"] = AppInfo.Description,
            },
            // instructions 会作为系统提示展示给模型，告诉它这个服务能做什么
            ["instructions"] =
                "这是一个运行在用户本机的个人 MCP 服务。你可以用它查询时间、做数学计算、" +
                "读写个人笔记、查看文件、处理文本以及抓取网页。调用工具前请先阅读工具说明，" +
                "参数不确定时优先使用可选参数的默认值。",
        };
    }

    /// <summary>
    /// 处理 tools/call：执行工具并包装结果。
    /// </summary>
    private async Task<JsonObject> CallToolAsync(
        JsonNode? id,
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        var name = parameters["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error(id, McpErrorCodes.InvalidParams, "tools/call 缺少工具名称（name）。");
        }

        var tool = _registry.Find(name);
        if (tool is null)
        {
            return Error(id, McpErrorCodes.InvalidParams, $"未知工具：{name}。可调用 tools/list 查看全部工具。");
        }

        // arguments 允许省略，视为空对象
        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();

        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteToolAsync(tool, arguments, cancellationToken);
        stopwatch.Stop();

        _logs.Add(name, arguments, result.IsError, result.Text, stopwatch.Elapsed.TotalMilliseconds, "mcp");

        var payload = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = result.Text,
                },
            },
            ["isError"] = result.IsError,
        };

        // 有结构化元数据时一并返回（规范里的 structuredContent，客户端可选用）
        if (result.Meta is not null)
        {
            payload["structuredContent"] = result.Meta.DeepClone();
        }

        return Success(id, payload);
    }

    /// <summary>
    /// 执行工具的统一入口：把「用户侧错误」转成 isError 结果，其余异常往上抛。
    /// 抽成静态方法，前端 REST 接口也能复用同一套行为。
    /// </summary>
    public static async Task<McpToolResult> ExecuteToolAsync(
        IMcpTool tool,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tool.InvokeAsync(arguments, cancellationToken);
        }
        catch (ToolException ex)
        {
            return McpToolResult.Fail(ex.Message);
        }
    }

    /// <summary>构造 resources/list 的结果：把每条笔记作为一个只读资源。</summary>
    private JsonObject BuildResourcesList()
    {
        var array = new JsonArray();

        foreach (var note in _notes.GetAll())
        {
            array.Add(new JsonObject
            {
                ["uri"] = NoteUriPrefix + note.Id,
                ["name"] = note.Title,
                ["title"] = note.Title,
                ["description"] = note.Content.Length > 80
                    ? note.Content[..80] + "…"
                    : note.Content,
                ["mimeType"] = "text/markdown",
            });
        }

        return new JsonObject { ["resources"] = array };
    }

    /// <summary>构造 resources/read 的结果：返回笔记正文。</summary>
    private JsonObject BuildResourceRead(JsonObject parameters)
    {
        var uri = parameters["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith(NoteUriPrefix, StringComparison.Ordinal))
        {
            throw new ToolException($"无法识别的资源 URI：{uri}。笔记资源格式为 {NoteUriPrefix}{{id}}。");
        }

        var noteId = uri[NoteUriPrefix.Length..];
        var note = _notes.Get(noteId)
                   ?? throw new ToolException($"笔记不存在：{noteId}");

        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = $"# {note.Title}\n\n{note.Content}\n\n---\n标签：{string.Join("、", note.Tags)}\n更新时间：{note.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
                },
            },
        };
    }

    /// <summary>构造 prompts/list：两个常用模板。</summary>
    private static JsonObject BuildPromptsList()
    {
        return new JsonObject
        {
            ["prompts"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "note_digest",
                    ["title"] = "整理个人笔记",
                    ["description"] = "让模型读取笔记并生成摘要、待办与主题分类。",
                    ["arguments"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "tag",
                            ["description"] = "只整理某个标签下的笔记，可留空表示全部。",
                            ["required"] = false,
                        },
                    },
                },
                new JsonObject
                {
                    ["name"] = "daily_brief",
                    ["title"] = "今日工作简报",
                    ["description"] = "结合当前时间与笔记，生成一段今日工作简报。",
                    ["arguments"] = new JsonArray(),
                },
            },
        };
    }

    /// <summary>构造 prompts/get：返回可以直接发送给模型的消息列表。</summary>
    private JsonObject BuildPromptGet(JsonObject parameters)
    {
        var name = parameters["name"]?.GetValue<string>();
        var args = parameters["arguments"] as JsonObject ?? new JsonObject();
        var tag = args["tag"]?.GetValue<string>();

        string text = name switch
        {
            "note_digest" => string.IsNullOrWhiteSpace(tag)
                ? "请调用 notes_list 读取我的全部笔记，然后输出：1) 三条要点摘要；2) 未完成的待办；3) 主题分类建议。"
                : $"请调用 notes_list（tag 参数设为「{tag}」）检索该标签下的笔记，然后输出要点摘要与后续行动建议。",
            "daily_brief" => "请调用 get_current_time 获取当前时间，再调用 notes_list 读取最近笔记，" +
                             "然后写一段 200 字以内的今日工作简报，语气简洁。",
            _ => throw new ToolException($"未知提示词模板：{name}"),
        };

        return new JsonObject
        {
            ["description"] = "由 McpDesk 提供的预置提示词",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text,
                    },
                },
            },
        };
    }

    /// <summary>构造成功响应。</summary>
    private static JsonObject Success(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = McpProtocol.JsonRpcVersion,
        ["id"] = id,
        ["result"] = result,
    };

    /// <summary>构造错误响应。</summary>
    private static JsonObject Error(JsonNode? id, int code, string message) => new()
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
