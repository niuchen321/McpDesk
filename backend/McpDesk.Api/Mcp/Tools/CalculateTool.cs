using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace McpDesk.Api.Mcp.Tools;

/// <summary>
/// 工具：数学计算。
///
/// 为什么不用 eval / DataTable.Compute？
///   · 前者在 .NET 里根本不存在（也不该用动态执行）；
///   · 后者依赖 System.Data 的表达式引擎，还容易受区域性设置影响。
/// 这里实现一个递归下降解析器：词法分析 → 语法分析 → 求值，全程可控、可测试，
/// 并且能给出「第几个字符出错」这种对模型友好的中文错误信息。
///
/// 支持的语法：
///   数字：1、3.14、1e-3
///   运算：+ - * / % ^（乘方，右结合）
///   括号：( )
///   常量：pi、e、tau
///   函数：sqrt、cbrt、abs、round、floor、ceil、trunc、sign、
///         sin、cos、tan、asin、acos、atan、ln、log、log2、exp、pow、min、max、hypot
/// </summary>
public sealed class CalculateTool : IMcpTool
{
    /// <summary>工具定义。</summary>
    public McpToolDefinition Definition { get; } = new()
    {
        Name = "calculate",
        Title = "数学计算",
        Description =
            "计算一个数学表达式并返回结果。支持 + - * / % ^、括号、常见函数" +
            "（sqrt、abs、round、sin、cos、ln、log、pow、min、max 等）与常量 pi、e、tau。" +
            "三角函数使用弧度。相比心算，模型应优先调用本工具以保证准确性。",
        InputSchema = new JsonSchemaBuilder()
            .AddString(
                "expression",
                "要计算的表达式，例如 (3+4)*2、sqrt(2)、pow(2,10)、sin(pi/6)。",
                required: true,
                maxLength: 500)
            .AddInteger(
                "precision",
                "结果保留的小数位数（0～15），默认 10。整数结果不受影响。",
                defaultValue: 10,
                minimum: 0,
                maximum: 15)
            .Build(),
    };

    /// <summary>执行工具。</summary>
    public Task<McpToolResult> InvokeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new ToolArgs(arguments);
        var expression = args.RequiredString("expression", "表达式");
        var precision = args.OptionalInt("precision", 10, 0, 15) ?? 10;

        var parser = new ExpressionParser(expression);
        var value = parser.Evaluate();

        if (double.IsNaN(value))
        {
            throw new ToolException($"表达式「{expression}」的计算结果为 NaN（非数字），请检查输入。");
        }

        if (double.IsInfinity(value))
        {
            throw new ToolException($"表达式「{expression}」的计算结果为无穷大，可能是除以 0 或数值溢出。");
        }

        var formatted = FormatNumber(value, precision);
        var text = $"{expression} = {formatted}";

        var meta = new JsonObject
        {
            ["expression"] = expression,
            ["value"] = value,
            ["formatted"] = formatted,
        };

        return Task.FromResult(McpToolResult.Ok(text, meta));
    }

    /// <summary>
    /// 数字格式化：
    ///   · 接近整数的结果直接显示为整数（避免 6.0000000001 这种噪音）；
    ///   · 其余按 precision 四舍五入并去掉末尾多余的 0。
    /// </summary>
    private static string FormatNumber(double value, int precision)
    {
        if (Math.Abs(value) < 1e15 && Math.Abs(value - Math.Round(value)) < 1e-12)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        var rounded = Math.Round(value, precision, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("F" + precision, CultureInfo.InvariantCulture);
        return text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }

    /// <summary>
    /// 递归下降表达式解析器。
    ///
    /// 文法（自上而下逐层解析，天然支持优先级）：
    ///   Expression := Term (('+' | '-') Term)*
    ///   Term       := Unary (('*' | '/' | '%') Unary)*
    ///   Unary      := ('+' | '-') Unary | Power
    ///   Power      := Primary ('^' Unary)?          ← 右结合：2^3^2 = 2^(3^2)
    ///   Primary    := Number | Identifier | Identifier '(' Args ')' | '(' Expression ')'
    /// </summary>
    private sealed class ExpressionParser
    {
        private readonly string _text;
        private int _pos;

        public ExpressionParser(string text)
        {
            _text = text ?? string.Empty;
        }

        /// <summary>解析并求值整个表达式。</summary>
        public double Evaluate()
        {
            if (string.IsNullOrWhiteSpace(_text))
            {
                throw new ToolException("表达式不能为空。");
            }

            var value = ParseExpression();

            SkipWhiteSpace();
            if (_pos < _text.Length)
            {
                throw Error($"无法识别的字符「{_text[_pos]}」");
            }

            return value;
        }

        // -----------------------------------------------------------------
        // 语法层
        // -----------------------------------------------------------------

        /// <summary>Expression := Term (('+' | '-') Term)*</summary>
        private double ParseExpression()
        {
            var left = ParseTerm();

            while (true)
            {
                SkipWhiteSpace();
                if (Match('+'))
                {
                    left += ParseTerm();
                }
                else if (Match('-'))
                {
                    left -= ParseTerm();
                }
                else
                {
                    return left;
                }
            }
        }

        /// <summary>Term := Unary (('*' | '/' | '%') Unary)*</summary>
        private double ParseTerm()
        {
            var left = ParseUnary();

            while (true)
            {
                SkipWhiteSpace();
                if (Match('*'))
                {
                    left *= ParseUnary();
                }
                else if (Match('/'))
                {
                    var right = ParseUnary();
                    if (right == 0)
                    {
                        throw Error("除数不能为 0");
                    }

                    left /= right;
                }
                else if (Match('%'))
                {
                    var right = ParseUnary();
                    if (right == 0)
                    {
                        throw Error("取模的除数不能为 0");
                    }

                    left %= right;
                }
                else
                {
                    return left;
                }
            }
        }

        /// <summary>Unary := ('+' | '-') Unary | Power</summary>
        private double ParseUnary()
        {
            SkipWhiteSpace();

            if (Match('+'))
            {
                return ParseUnary();
            }

            if (Match('-'))
            {
                return -ParseUnary();
            }

            return ParsePower();
        }

        /// <summary>Power := Primary ('^' Unary)? —— '^' 右结合</summary>
        private double ParsePower()
        {
            var baseValue = ParsePrimary();

            SkipWhiteSpace();
            if (Match('^'))
            {
                var exponent = ParseUnary();
                return Math.Pow(baseValue, exponent);
            }

            return baseValue;
        }

        /// <summary>Primary := Number | Constant | Function '(' Args ')' | '(' Expression ')'</summary>
        private double ParsePrimary()
        {
            SkipWhiteSpace();

            if (Match('('))
            {
                var inner = ParseExpression();
                SkipWhiteSpace();
                if (!Match(')'))
                {
                    throw Error("缺少右括号 ')'");
                }

                return inner;
            }

            // 数字：整数、小数、科学计数法
            if (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                return ParseNumber();
            }

            // 标识符：常量或函数名
            if (_pos < _text.Length && (char.IsLetter(_text[_pos]) || _text[_pos] == '_'))
            {
                return ParseIdentifier();
            }

            throw Error(_pos >= _text.Length ? "表达式意外结束" : $"无法解析的内容「{_text[_pos]}」");
        }

        // -----------------------------------------------------------------
        // 词法层
        // -----------------------------------------------------------------

        /// <summary>解析一个数字字面量。</summary>
        private double ParseNumber()
        {
            var start = _pos;

            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                _pos++;
            }

            // 科学计数法：1e10、2.5E-3
            if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                var save = _pos;
                _pos++;
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
                {
                    _pos++;
                }

                if (_pos < _text.Length && char.IsDigit(_text[_pos]))
                {
                    while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                    {
                        _pos++;
                    }
                }
                else
                {
                    // 不是科学计数法（例如 "2e" 后面不是数字），回退
                    _pos = save;
                }
            }

            var token = _text[start.._pos];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw Error($"无效的数字「{token}」");
            }

            return value;
        }

        /// <summary>解析常量或函数调用。</summary>
        private double ParseIdentifier()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
            {
                _pos++;
            }

            var name = _text[start.._pos].ToLowerInvariant();

            SkipWhiteSpace();

            // 带括号 → 函数调用
            if (Match('('))
            {
                var arguments = new List<double>();

                SkipWhiteSpace();
                if (!Match(')'))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                        SkipWhiteSpace();
                    }
                    while (Match(','));

                    if (!Match(')'))
                    {
                        throw Error($"函数 {name} 缺少右括号 ')'");
                    }
                }

                return ApplyFunction(name, arguments);
            }

            // 不带括号 → 常量
            return name switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                "tau" => Math.Tau,
                _ => throw Error($"未知的标识符「{name}」。常量支持 pi、e、tau；函数需要写成 name(...) 形式"),
            };
        }

        /// <summary>执行函数调用，并检查参数个数。</summary>
        private double ApplyFunction(string name, List<double> args)
        {
            double Need(int count)
            {
                if (args.Count != count)
                {
                    throw Error($"函数 {name} 需要 {count} 个参数，实际传入 {args.Count} 个");
                }

                return args[0];
            }

            return name switch
            {
                "sqrt" => Math.Sqrt(Need(1)),
                "cbrt" => Math.Cbrt(Need(1)),
                "abs" => Math.Abs(Need(1)),
                "round" => Math.Round(Need(1), MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(Need(1)),
                "ceil" => Math.Ceiling(Need(1)),
                "trunc" => Math.Truncate(Need(1)),
                "sign" => Math.Sign(Need(1)),
                "sin" => Math.Sin(Need(1)),
                "cos" => Math.Cos(Need(1)),
                "tan" => Math.Tan(Need(1)),
                "asin" => Math.Asin(Need(1)),
                "acos" => Math.Acos(Need(1)),
                "atan" => Math.Atan(Need(1)),
                "ln" => Math.Log(Need(1)),
                "log" => Math.Log10(Need(1)),
                "log2" => Math.Log2(Need(1)),
                "exp" => Math.Exp(Need(1)),
                "pow" => args.Count == 2 ? Math.Pow(args[0], args[1]) : throw Error("pow 需要 2 个参数：pow(底数, 指数)"),
                "min" => args.Count >= 1 ? args.Min() : throw Error("min 至少需要 1 个参数"),
                "max" => args.Count >= 1 ? args.Max() : throw Error("max 至少需要 1 个参数"),
                "hypot" => args.Count == 2 ? Math.Sqrt(args[0] * args[0] + args[1] * args[1]) : throw Error("hypot 需要 2 个参数"),
                _ => throw Error($"未知函数「{name}」"),
            };
        }

        // -----------------------------------------------------------------
        // 辅助方法
        // -----------------------------------------------------------------

        /// <summary>跳过空格与制表符。</summary>
        private void SkipWhiteSpace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        /// <summary>如果当前字符是 c，则消费它并返回 true。</summary>
        private bool Match(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        /// <summary>生成带位置提示的中文错误信息，例如「第 5 个字符附近：缺少右括号」。</summary>
        private ToolException Error(string message)
        {
            var caret = _pos < _text.Length ? _pos + 1 : _text.Length;
            var snippet = BuildSnippet();
            return new ToolException($"表达式解析失败（第 {caret} 个字符附近）：{message}。\n位置提示：{snippet}");
        }

        /// <summary>用 ^ 标出出错位置，帮助模型/用户定位问题。</summary>
        private string BuildSnippet()
        {
            var builder = new StringBuilder();
            builder.AppendLine(_text);

            var caretIndex = Math.Clamp(_pos, 0, _text.Length);
            for (var i = 0; i < caretIndex; i++)
            {
                builder.Append(' ');
            }

            builder.Append('^');
            return builder.ToString();
        }
    }
}
