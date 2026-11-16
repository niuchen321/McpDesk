namespace McpDesk.Api.Services;

/// <summary>
/// 应用配置项，对应 appsettings.json 里的 "McpDesk" 节点。
///
/// 示例：
/// "McpDesk": {
///   "ApiKey": "",                       // 留空表示本机免鉴权
///   "StorageRoot": "data",              // 笔记等数据存放目录（相对内容根目录）
///   "FileSandboxRoot": "",              // 文件类工具可访问的根目录；留空则使用当前用户目录
///   "MaxFetchBytes": 1000000,           // http_get 单次最多读取多少字节
///   "MaxLogEntries": 200                // 内存中保留多少条调用日志
/// }
/// </summary>
public sealed class AppOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "McpDesk";

    /// <summary>
    /// 可选的访问密钥。非空时，MCP 端点要求请求头 X-Api-Key 与之相同。
    /// 本机自用建议留空；把服务暴露到局域网时请务必设置。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>数据目录（笔记 JSON 文件存放位置），相对路径以内容根目录为基准。</summary>
    public string StorageRoot { get; set; } = "data";

    /// <summary>
    /// 文件类工具（list_files / read_text_file）允许访问的根目录。
    /// 留空时默认使用当前用户目录，防止 AI 误读系统其它位置。
    /// </summary>
    public string? FileSandboxRoot { get; set; }

    /// <summary>抓取网页时最多读取的字节数，防止一次性拉取超大文件。</summary>
    public int MaxFetchBytes { get; set; } = 1_000_000;

    /// <summary>
    /// 是否允许 http_get 访问内网 / 本机地址。
    ///
    /// 默认 false：模型可能被网页内容诱导去访问 http://192.168.x.x 这类内网地址，
    /// 从而读取本机或局域网里的敏感接口（SSRF）。个人确实需要调试本地服务时再打开。
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }

    /// <summary>调用日志在内存中保留的最大条数（超出后丢弃最旧的）。</summary>
    public int MaxLogEntries { get; set; } = 200;
}
