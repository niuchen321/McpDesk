using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp;

/// <summary>
/// 用链式调用拼装 JSON Schema 的小工具。
///
/// 为什么不直接写 JSON 字符串？
///   · 手写容易漏括号、拼错字段，编译器无法帮忙检查；
///   · 前端需要读取 properties 来自动生成表单，结构必须一致；
///   · 链式写法在工具类里一眼就能看出「这个工具要哪些参数」。
///
/// 生成的 Schema 形如：
/// {
///   "type": "object",
///   "properties": {
///     "title": { "type": "string", "description": "笔记标题", "maxLength": 120 }
///   },
///   "required": ["title"],
///   "additionalProperties": false
/// }
/// </summary>
public sealed class JsonSchemaBuilder
{
    private readonly JsonObject _properties = new();
    private readonly JsonArray _required = new();

    /// <summary>添加一个字符串参数。</summary>
    /// <param name="name">参数名（与 C# 方法参数一样，建议小写 + 下划线或驼峰）。</param>
    /// <param name="description">参数说明（中文），会显示在前端表单与模型提示中。</param>
    /// <param name="required">是否必填。</param>
    /// <param name="defaultValue">默认值，前端会预填。</param>
    /// <param name="minLength">最小长度。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <param name="format">可选的语义格式，如 "date"、"uri"。</param>
    public JsonSchemaBuilder AddString(
        string name,
        string description,
        bool required = false,
        string? defaultValue = null,
        int? minLength = null,
        int? maxLength = null,
        string? format = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
        };
        if (defaultValue is not null) schema["default"] = defaultValue;
        if (minLength.HasValue) schema["minLength"] = minLength.Value;
        if (maxLength.HasValue) schema["maxLength"] = maxLength.Value;
        if (!string.IsNullOrWhiteSpace(format)) schema["format"] = format;

        return AddProperty(name, schema, required);
    }

    /// <summary>添加一个整数参数。</summary>
    public JsonSchemaBuilder AddInteger(
        string name,
        string description,
        bool required = false,
        int? defaultValue = null,
        int? minimum = null,
        int? maximum = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description,
        };
        if (defaultValue.HasValue) schema["default"] = defaultValue.Value;
        if (minimum.HasValue) schema["minimum"] = minimum.Value;
        if (maximum.HasValue) schema["maximum"] = maximum.Value;

        return AddProperty(name, schema, required);
    }

    /// <summary>添加一个数字（小数）参数。</summary>
    public JsonSchemaBuilder AddNumber(
        string name,
        string description,
        bool required = false,
        double? defaultValue = null,
        double? minimum = null,
        double? maximum = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "number",
            ["description"] = description,
        };
        if (defaultValue.HasValue) schema["default"] = defaultValue.Value;
        if (minimum.HasValue) schema["minimum"] = minimum.Value;
        if (maximum.HasValue) schema["maximum"] = maximum.Value;

        return AddProperty(name, schema, required);
    }

    /// <summary>添加一个布尔参数。</summary>
    public JsonSchemaBuilder AddBoolean(
        string name,
        string description,
        bool required = false,
        bool? defaultValue = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = description,
        };
        if (defaultValue.HasValue) schema["default"] = defaultValue.Value;

        return AddProperty(name, schema, required);
    }

    /// <summary>添加一个枚举参数（前端可渲染为下拉框）。</summary>
    /// <param name="name">参数名。</param>
    /// <param name="description">参数说明（会展示给模型）。</param>
    /// <param name="values">允许的取值集合。</param>
    /// <param name="required">是否必填。</param>
    /// <param name="defaultValue">默认值，可为 null。</param>
    public JsonSchemaBuilder AddEnum(
        string name,
        string description,
        IEnumerable<string> values,
        bool required = false,
        string? defaultValue = null)
    {
        var enumArray = new JsonArray();
        foreach (var value in values) enumArray.Add(value);

        var schema = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = enumArray,
        };
        if (defaultValue is not null) schema["default"] = defaultValue;

        return AddProperty(name, schema, required);
    }

    /// <summary>添加一个字符串数组参数（多值标签）。</summary>
    public JsonSchemaBuilder AddStringArray(
        string name,
        string description,
        bool required = false,
        int? maxItems = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new JsonObject { ["type"] = "string" },
        };
        if (maxItems.HasValue) schema["maxItems"] = maxItems.Value;

        return AddProperty(name, schema, required);
    }

    /// <summary>生成最终的 Schema 对象。</summary>
    public JsonObject Build()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = _properties,
            // 禁止额外字段：客户端传错参数名时能尽早发现，也避免模型"编造"参数
            ["additionalProperties"] = false,
        };

        if (_required.Count > 0)
        {
            schema["required"] = _required;
        }

        return schema;
    }

    /// <summary>把属性加入 properties，并按需登记 required。</summary>
    private JsonSchemaBuilder AddProperty(string name, JsonObject schema, bool required)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("参数名不能为空", nameof(name));
        }

        if (_properties.ContainsKey(name))
        {
            throw new ArgumentException($"参数名重复：{name}", nameof(name));
        }

        _properties[name] = schema;
        if (required)
        {
            _required.Add(name);
        }

        return this;
    }
}
