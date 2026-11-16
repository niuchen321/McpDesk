using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp;

/// <summary>
/// 工具注册表：把 DI 容器里所有 <see cref="IMcpTool"/> 聚合成「名称 → 实例」的字典，
/// 供 tools/list、tools/call 以及前端 /api/tools 查询。
///
/// 由于注册阶段已按类型名排序，列表顺序稳定，便于前端与快照对比。
/// </summary>
public sealed class McpToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools;

    /// <summary>
    /// 构造函数由 DI 注入全部工具实现。
    /// 若出现同名工具会立即抛异常，让问题在启动阶段暴露，而不是运行时静默覆盖。
    /// </summary>
    public McpToolRegistry(IEnumerable<IMcpTool> tools)
    {
        _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            var name = tool.Definition.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"工具 {tool.GetType().Name} 未定义名称。");
            }

            if (!_tools.TryAdd(name, tool))
            {
                throw new InvalidOperationException(
                    $"工具名称重复：{name}。请检查 {tool.GetType().Name} 与 {_tools[name].GetType().Name}。");
            }
        }
    }

    /// <summary>全部工具（按名称排序）。</summary>
    public IReadOnlyList<IMcpTool> All =>
        _tools.Values.OrderBy(t => t.Definition.Name, StringComparer.Ordinal).ToList();

    /// <summary>按名称查找工具，找不到返回 null。</summary>
    public IMcpTool? Find(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// 生成 MCP tools/list 所需的数组。
    /// 字段名遵循 MCP 规范：name / title / description / inputSchema。
    /// </summary>
    public JsonArray BuildToolsJsonArray()
    {
        var array = new JsonArray();

        foreach (var tool in All)
        {
            var definition = tool.Definition;
            array.Add(new JsonObject
            {
                ["name"] = definition.Name,
                ["title"] = definition.Title ?? definition.Name,
                ["description"] = definition.Description,
                ["inputSchema"] = definition.InputSchema.DeepClone(),
            });
        }

        return array;
    }
}
