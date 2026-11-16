using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using McpDesk.Api.Mcp;
using McpDesk.Api.Services;

// =====================================================================================
//  McpDesk.Api 启动入口
//  -----------------------------------------------------------------------------------
//  这个程序同时承担两件事：
//    1) 作为「MCP Server」：在 POST /mcp 上实现 MCP 协议（JSON-RPC 2.0 over Streamable HTTP），
//       供 Claude Desktop、Cursor、VS Code 等支持 MCP 的客户端调用。
//    2) 作为「管理后台的 API」：在 /api/* 上提供工具列表、调用、日志、笔记等 REST 接口，
//       给 frontend（Vue 3）调用。
//
//  整体设计要点：
//    · 工具（Tool）通过反射自动发现并注册，新增一个工具类即可，无需改任何注册代码；
//    · 不依赖任何第三方 NuGet 包，纯 ASP.NET Core 内置能力，可离线还原；
//    · 生产部署时，把前端构建产物放到 wwwroot，即可用一个进程同时提供页面与接口。
// =====================================================================================

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------------------------------------------
// 1. 读取配置（appsettings.json 中的 McpDesk 节点）
// -------------------------------------------------------------------------------------
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

// -------------------------------------------------------------------------------------
// 2. 注册业务服务
//    · NoteStore      ：个人笔记的持久化（JSON 文件存储，线程安全）
//    · CallLogStore   ：最近若干次工具调用的内存日志，供前端调试面板查看
//    · FileSandbox    ：文件访问沙盒，所有文件类工具都必须经过它做路径校验
//    · McpToolRegistry：工具注册表，按名称索引所有 [工具]
// -------------------------------------------------------------------------------------
builder.Services.AddSingleton<NoteStore>();
builder.Services.AddSingleton<CallLogStore>();
builder.Services.AddSingleton<FileSandbox>();
builder.Services.AddSingleton<McpToolRegistry>();

// MCP 请求处理器按请求创建，内部持有本次请求的上下文（如调用耗时统计）
builder.Services.AddScoped<McpRequestHandler>();

// -------------------------------------------------------------------------------------
// 3. 注册 HTTP 客户端（供 http_get 工具抓取网页使用）
//    统一设置超时与 User-Agent，避免每个工具各自 new HttpClient。
// -------------------------------------------------------------------------------------
builder.Services.AddHttpClient("web-fetch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("McpDesk/1.0 (+personal MCP server)");
});

// -------------------------------------------------------------------------------------
// 4. 自动注册所有工具
//    约定：任何实现 IMcpTool 的非抽象类，都会被注册为单例并从 DI 容器中解析。
//    这样新增工具时只需要在 Mcp/Tools 下加一个类即可，符合「开闭原则」。
// -------------------------------------------------------------------------------------
var toolTypes = typeof(Program).Assembly
    .GetTypes()
    .Where(t => typeof(IMcpTool).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
    .OrderBy(t => t.Name, StringComparer.Ordinal)
    .ToList();

foreach (var toolType in toolTypes)
{
    // 以 IMcpTool 接口注册，注入方通过 IEnumerable<IMcpTool> 拿到全部实现
    builder.Services.AddSingleton(typeof(IMcpTool), toolType);
}

// -------------------------------------------------------------------------------------
// 5. MVC / JSON 配置
//    · 控制器用于 /api/* 的 REST 接口
//    · Encoder 放开中文转义，接口返回的 JSON 里中文直接可读（不会被转成 \uXXXX）
// -------------------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.WriteIndented = false;
    });

// -------------------------------------------------------------------------------------
// 6. CORS（仅开发期需要：Vite 开发服务器跑在 5173 端口）
// -------------------------------------------------------------------------------------
const string DevCorsPolicy = "dev-cors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// -------------------------------------------------------------------------------------
// 7. 中间件管道
//    UseDefaultFiles + UseStaticFiles：托管 wwwroot 下前端构建产物
//    MapControllers：/api/* REST 接口
// -------------------------------------------------------------------------------------
app.UseCors(DevCorsPolicy);
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// -------------------------------------------------------------------------------------
// 8. MCP 协议端点
//    POST /mcp ：客户端发送 JSON-RPC 请求，服务端同步返回 JSON-RPC 响应
//    GET  /mcp ：按 MCP Streamable HTTP 规范用于建立服务端推送流；本服务不主动推送，
//                因此返回 405 并给出提示（客户端会自动回退到 POST 模式）
// -------------------------------------------------------------------------------------
app.MapPost("/mcp", McpEndpoint.HandleAsync);
app.MapGet("/mcp", () => Results.Json(new
{
    error = "本服务不支持服务端事件流（GET /mcp）。请使用 POST /mcp 发送 JSON-RPC 请求。",
}, statusCode: StatusCodes.Status405MethodNotAllowed));

// -------------------------------------------------------------------------------------
// 9. 健康检查：给前端做「服务是否在线」探测，也可用于容器/守护进程探活
// -------------------------------------------------------------------------------------
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    server = AppInfo.Name,
    version = AppInfo.Version,
    protocolVersion = McpProtocol.Version,
    toolCount = app.Services.GetRequiredService<McpToolRegistry>().All.Count,
    utcTime = DateTimeOffset.UtcNow,
}));

// -------------------------------------------------------------------------------------
// 10. 单页应用回退
//     把未匹配的路径都交给 index.html，由前端根据 URL hash 切换页面。
//     这里不做「index.html 是否存在」的判断：尚未构建前端时，该文件不存在，
//     请求会自然返回 404，而不是让服务启动失败；构建完成后无需重启即可生效。
//
//     注意：必须让未匹配的 /api/* 先返回 JSON 404，否则会被回退成 HTML，
//     让调用方误以为接口存在、只是返回了网页。
// -------------------------------------------------------------------------------------
app.MapMethods("/api/{**rest}", new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" },
    () => Results.NotFound(new { error = "请求的接口不存在。" }));

app.MapFallbackToFile("index.html");

app.Run();
