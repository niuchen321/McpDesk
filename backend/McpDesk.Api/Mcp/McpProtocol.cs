namespace McpDesk.Api.Mcp;

/// <summary>
/// MCP（Model Context Protocol）协议相关的常量集合。
///
/// MCP 的传输层是一问一答的 JSON-RPC 2.0：
///   客户端 → 服务端：{"jsonrpc":"2.0","id":1,"method":"tools/list"}
///   服务端 → 客户端：{"jsonrpc":"2.0","id":1,"result":{...}}
///
/// 本类集中存放协议版本号与标准错误码，避免在代码各处写魔法字符串。
/// </summary>
public static class McpProtocol
{
    /// <summary>
    /// 服务端当前实现的 MCP 协议版本（2025-06-18 是 MCP 规范的稳定版本）。
    /// 客户端在 initialize 时会带上它支持的版本，双方取交集；若客户端版本不在
    /// <see cref="SupportedVersions"/> 中，服务端返回本版本号，由客户端决定是否继续。
    /// </summary>
    public const string Version = "2025-06-18";

    /// <summary>
    /// 兼容的协议版本列表（发布越新的版本放在越前面）。
    /// </summary>
    public static readonly string[] SupportedVersions =
    {
        "2025-06-18",
        "2025-03-26",
        "2024-11-05",
    };

    /// <summary>JSON-RPC 2.0 规定的协议标识。</summary>
    public const string JsonRpcVersion = "2.0";
}

/// <summary>
/// JSON-RPC 2.0 标准错误码，以及 MCP 规范补充的错误码。
/// 参考：https://www.jsonrpc.org/specification#error_object
/// </summary>
public static class McpErrorCodes
{
    /// <summary>解析错误：收到的内容不是合法 JSON。</summary>
    public const int ParseError = -32700;

    /// <summary>非法请求：JSON 合法但不是合法的 JSON-RPC 请求对象。</summary>
    public const int InvalidRequest = -32600;

    /// <summary>方法不存在：客户端调用了服务端未实现的 method。</summary>
    public const int MethodNotFound = -32601;

    /// <summary>参数错误：method 的参数缺失、类型不对或取值越界。</summary>
    public const int InvalidParams = -32602;

    /// <summary>服务端内部错误：处理过程中抛出未预期的异常。</summary>
    public const int InternalError = -32603;
}

/// <summary>
/// 服务的身份信息（在 initialize 响应与 /health 接口中返回）。
/// 修改变量名即可整体改名，无需搜索替换。
/// </summary>
public static class AppInfo
{
    /// <summary>服务名，MCP 客户端会把它显示给用户。</summary>
    public const string Name = "McpDesk";

    /// <summary>对外展示的标题（中文）。</summary>
    public const string Title = "McpDesk 个人 MCP 服务台";

    /// <summary>服务版本号，语义化版本格式。</summary>
    public const string Version = "1.0.0";

    /// <summary>服务说明，会出现在 initialize 响应里。</summary>
    public const string Description =
        "一个运行在本机的个人 MCP 服务器：提供时间、计算、笔记、文件、文本处理等工具，" +
        "并通过 Vue 管理后台进行可视化调试。";
}
