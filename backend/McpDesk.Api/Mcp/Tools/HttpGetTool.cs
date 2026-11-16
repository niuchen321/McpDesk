using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using McpDesk.Api.Services;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：抓取网页 / HTTP 接口内容（GET）。
///
/// 这是最容易出问题的地方，所以做了三层限制：
///   1. 只允许 http / https 两种协议；
///   2. 默认拒绝访问本机与内网地址（SSRF 防护），可在 appsettings 里开关；
///   3. 只读取前 MaxFetchBytes 个字节，且只处理文本类 Content-Type。
/// </summary>
public sealed class HttpGetTool : IMcpTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppOptions _options;
    private readonly ILogger<HttpGetTool> _logger;

    /// <summary>构造函数：注入 HTTP 客户端工厂与配置。</summary>
    public HttpGetTool(IHttpClientFactory httpClientFactory, IOptions<AppOptions> options, ILogger<HttpGetTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "http_get",
        Title = "抓取网页",
        Description =
            "用 GET 请求抓取一个网页或 HTTP 接口，返回响应状态码、Content-Type 与文本内容。" +
            "默认禁止访问本机和内网地址，单次最多返回约 1 MB 文本。",
        InputSchema = new JsonSchemaBuilder()
            .AddString("url", "完整地址，必须以 http:// 或 https:// 开头。", required: true, maxLength: 2000, format: "uri")
            .AddInteger("maxBytes", "最多读取的字节数（1024～2000000），默认取服务配置值。", minimum: 1024, maximum: 2_000_000)
            .AddBoolean("followRedirects", "是否自动跟随重定向，默认 true。", defaultValue: true)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public async Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var urlText = args.RequiredString("url", "网址").Trim();
        var maxBytes = args.OptionalInt("maxBytes", _options.MaxFetchBytes, 1024, 2_000_000) ?? _options.MaxFetchBytes;
        var followRedirects = args.OptionalBool("followRedirects", true);

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
        {
            throw new ToolException($"网址格式不合法：{urlText}");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ToolException($"只支持 http / https 协议，当前是：{uri.Scheme}");
        }

        if (!_options.AllowPrivateNetwork && IsPrivateOrLocal(uri))
        {
            throw new ToolException(
                $"出于安全考虑，默认禁止抓取本机或内网地址：{uri.Host}。" +
                "如确实需要，请在 appsettings.json 中把 McpDesk:AllowPrivateNetwork 设为 true。");
        }

        // 工厂返回的客户端已配置好超时与 User-Agent，优先使用；
        // 需要「不跟随重定向」时才单独创建临时客户端。
        using var pooledClient = followRedirects ? _httpClientFactory.CreateClient("web-fetch") : null;
        using var noRedirectClient = followRedirects ? null : CreateNoRedirectClient();

        var http = (HttpClient?)pooledClient ?? noRedirectClient!;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("text/html,application/json,text/plain,*/*;q=0.8");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ToolException($"请求超时：{uri}");
        }
        catch (HttpRequestException ex)
        {
            throw new ToolException($"请求失败：{ex.Message}");
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "(未知)";
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine($"地址：{uri}");
            builder.AppendLine($"状态：{statusCode} {response.ReasonPhrase}");
            builder.AppendLine($"Content-Type：{contentType}");

            if (response.Headers.Location is not null && !followRedirects)
            {
                builder.AppendLine($"重定向到：{response.Headers.Location}");
            }

            // 只处理文本类响应，二进制内容（图片、压缩包）直接拒绝
            var isText = mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                         || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
                         || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                         || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                         || mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                         || mediaType.Length == 0;

            if (!isText)
            {
                builder.AppendLine();
                builder.AppendLine("（响应不是文本内容，已跳过正文。）");
                return McpToolResult.Ok(builder.ToString().TrimEnd(), new JsonObject
                {
                    ["url"] = uri.ToString(),
                    ["status"] = statusCode,
                    ["contentType"] = contentType,
                    ["truncated"] = false,
                    ["bytesRead"] = 0,
                });
            }

            builder.AppendLine(new string('-', 40));

            var (text, bytesRead, truncated, encodingName) = await ReadLimitedTextAsync(response, maxBytes, cancellationToken);

            builder.AppendLine(text);

            if (truncated)
            {
                builder.AppendLine(new string('-', 40));
                builder.AppendLine($"（内容超过 {maxBytes} 字节，已截断。）");
            }

            _logger.LogInformation("http_get {Url} -> {Status}，读取 {Bytes} 字节", uri, statusCode, bytesRead);

            var meta = new JsonObject
            {
                ["url"] = uri.ToString(),
                ["status"] = statusCode,
                ["contentType"] = contentType,
                ["encoding"] = encodingName,
                ["bytesRead"] = bytesRead,
                ["truncated"] = truncated,
            };

            return McpToolResult.Ok(builder.ToString().TrimEnd(), meta);
        }
    }

    /// <summary>
    /// 读取响应正文，最多 maxBytes 个字节。
    /// 返回：文本、实际字节数、是否被截断、检测到的编码。
    /// </summary>
    private static async Task<(string Text, int BytesRead, bool Truncated, string Encoding)> ReadLimitedTextAsync(
        HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[maxBytes];
        var total = 0;

        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        // 再尝试读 1 个字节，判断是否真的还有内容
        var extra = new byte[1];
        var hasMore = await stream.ReadAsync(extra.AsMemory(0, 1), cancellationToken) > 0;

        // 注意：异步方法里不能声明 Span 局部变量，所以解码放到同步辅助方法中完成
        var text = DecodeText(buffer, total, response.Content.Headers.ContentType?.CharSet, out var encodingName);

        return (text, total, hasMore, encodingName);
    }

    /// <summary>
    /// 用检测到的编码把前 total 个字节解码为文本。
    /// 抽成同步方法的原因：Span 不能在 async 方法中使用。
    /// </summary>
    private static string DecodeText(byte[] buffer, int total, string? charset, out string encodingName)
    {
        var span = buffer.AsSpan(0, total);
        var encoding = DetectEncoding(span, charset);
        encodingName = encoding.WebName;
        return encoding.GetString(span);
    }

    /// <summary>根据 BOM 或响应头里的 charset 判断编码，默认 UTF-8。</summary>
    private static Encoding DetectEncoding(ReadOnlySpan<byte> bytes, string? charset)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // 未知 charset，回退到 UTF-8
            }
        }

        return new UTF8Encoding(false);
    }

    /// <summary>创建一个不自动跟随重定向的临时客户端。</summary>
    private static HttpClient CreateNoRedirectClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McpDesk/1.0 (+personal MCP server)");
        return client;
    }

    /// <summary>
    /// 判断 URI 是否指向本机或内网地址。
    ///
    /// 说明：这里只做「字面量」判断（主机名为 localhost、IP 落在内网段），
    /// 足以拦住大部分误操作；如果对手是刻意构造的 DNS 重绑定攻击，还需要在
    /// 连接层做二次校验，个人使用场景下这里已经足够。
    /// </summary>
    private static bool IsPrivateOrLocal(Uri uri)
    {
        var host = uri.Host;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            // 域名无法直接判断，交给后续的 DNS 解析（此处按公网处理）
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // IPv6 唯一本地地址 fc00::/7、链路本地 fe80::/10
            var v6 = ip.GetAddressBytes();
            return (v6[0] & 0xFE) == 0xFC || (v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80);
        }

        var b = ip.GetAddressBytes();

        return b[0] == 10                                 // 10.0.0.0/8
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)  // 172.16.0.0/12
               || (b[0] == 192 && b[1] == 168)             // 192.168.0.0/16
               || (b[0] == 169 && b[1] == 254)             // 169.254.0.0/16 链路本地
               || b[0] == 127                             // 127.0.0.0/8
               || b[0] == 0;                              // 0.0.0.0/8
    }
}
