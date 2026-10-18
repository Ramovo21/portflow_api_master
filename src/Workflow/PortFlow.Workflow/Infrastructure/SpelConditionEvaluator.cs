using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain;

namespace PortFlow.Workflow.Infrastructure;

/// <summary>
/// Đánh giá điều kiện bước theo cú pháp SpEL – tập con an toàn tương đương <c>SimpleEvaluationContext</c> chỉ đọc
/// của bản Java: literal (số, 'chuỗi', true/false/null), biến trong <see cref="IConditionEvaluator.Variables"/>,
/// <c>== != &lt; &lt;= &gt; &gt;= eq ne lt le gt ge matches</c>, <c>and or not &amp;&amp; || !</c>,
/// <c>+ - * / % div mod</c>, <c>?:</c>, ngoặc. Không có gọi method, <c>T(...)</c>, <c>@bean</c>, gán, truy cập thuộc tính.
/// </summary>
internal sealed class SpelConditionEvaluator : IConditionEvaluator
{
    private const int MaxLength = 500;
    private const int MaxCacheSize = 1_000;

    private readonly ConcurrentDictionary<string, Node> _cache = new();

    public bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }
        var safe = vars
            .Where(entry => IConditionEvaluator.Variables.Contains(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        object? result;
        try
        {
            if (expression.Length > MaxLength)
            {
                throw new ConditionException($"Expression exceeds maximum length of {MaxLength}");
            }
            var node = _cache.TryGetValue(expression, out var cached) ? cached : Parse(expression);
            if (_cache.Count < MaxCacheSize)
            {
                _cache.TryAdd(expression, node);
            }
            result = node.Evaluate(safe);
        }
        catch (ConditionException ex)
        {
            throw Error(expression, ex.Message);
        }
        return result is bool value ? value : throw Error(expression, "result is not true/false: " + Show(result));
    }

    private static Node Parse(string expression)
    {
        var parser = new Parser(Tokenize(expression));
        var node = parser.ParseExpression();
        parser.ExpectEnd();
        return node;
    }

    private static DomainException Error(string expression, string cause) =>
        new(WorkflowError.WF_CONDITION_ERROR, "Step condition could not be evaluated: " + expression,
            new Dictionary<string, object?> { ["expression"] = expression, ["cause"] = cause });

    private static string Show(object? value) => value switch
    {
        null => "null",
        string text => "'" + text + "'",
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private sealed class ConditionException(string message) : Exception(message);

    // ===== Tokenizer =====

    private enum TokenKind
    {
        Number,
        String,
        Identifier,
        Operator,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Position, object? Value = null);

    private static readonly string[] Operators = ["?:", "==", "!=", "<=", ">=", "&&", "||", "<", ">", "!", "+", "-", "*", "/", "%", "(", ")", "?", ":"];

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }
                if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }
                }
                if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                {
                    var exponent = i + 1;
                    if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-'))
                    {
                        exponent++;
                    }
                    if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                    {
                        i = exponent;
                        while (i < text.Length && char.IsAsciiDigit(text[i]))
                        {
                            i++;
                        }
                    }
                }
                if (i < text.Length && (text[i] is 'L' or 'l' or 'd' or 'D' or 'f' or 'F'))
                {
                    i++;
                }
                var literal = text[start..i].TrimEnd('L', 'l', 'd', 'D', 'f', 'F');
                tokens.Add(new Token(TokenKind.Number, text[start..i], start,
                    decimal.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture)));
                continue;
            }
            if (c is '\'' or '"')
            {
                var start = i;
                var value = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= text.Length)
                    {
                        throw new ConditionException($"Unterminated string at position {start}");
                    }
                    if (text[i] == c)
                    {
                        if (i + 1 < text.Length && text[i + 1] == c)
                        {
                            value.Append(c);
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    value.Append(text[i++]);
                }
                tokens.Add(new Token(TokenKind.String, text[start..i], start, value.ToString()));
                continue;
            }
            if (char.IsAsciiLetter(c) || c is '_' or '$')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '$'))
                {
                    i++;
                }
                tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
                continue;
            }
            var op = Operators.FirstOrDefault(candidate => string.CompareOrdinal(text, i, candidate, 0, candidate.Length) == 0);
            if (op is null)
            {
                throw new ConditionException($"Unexpected character '{c}' at position {i}");
            }
            tokens.Add(new Token(TokenKind.Operator, op, i));
            i += op.Length;
        }
        tokens.Add(new Token(TokenKind.End, "", text.Length));
        return tokens;
    }

    // ===== Parser =====

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        private Token Current => tokens[_index];

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw new ConditionException($"Unexpected '{Current.Text}' at position {Current.Position}");
            }
        }

        public Node ParseExpression()
        {
            var condition = ParseOr();
            if (IsOperator("?:"))
            {
                _index++;
                return new Elvis(condition, ParseExpression());
            }
            if (IsOperator("?"))
            {
                _index++;
                var whenTrue = ParseExpression();
                Expect(":");
                return new Ternary(condition, whenTrue, ParseExpression());
            }
            return condition;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (IsOperator("||") || IsKeyword("or"))
            {
                _index++;
                left = new Logical(left, ParseAnd(), isAnd: false);
            }
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseRelational();
            while (IsOperator("&&") || IsKeyword("and"))
            {
                _index++;
                left = new Logical(left, ParseRelational(), isAnd: true);
            }
            return left;
        }

        private Node ParseRelational()
        {
            var left = ParseSum();
            var op = RelationalOperator();
            if (op is null)
            {
                return left;
            }
            _index++;
            return new Relational(op, left, ParseSum());
        }

        private string? RelationalOperator()
        {
            if (Current.Kind == TokenKind.Operator && Current.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                return Current.Text;
            }
            if (Current.Kind == TokenKind.Identifier)
            {
                return Current.Text.ToLowerInvariant() switch
                {
                    "eq" => "==",
                    "ne" => "!=",
                    "lt" => "<",
                    "le" => "<=",
                    "gt" => ">",
                    "ge" => ">=",
                    "matches" => "matches",
                    _ => null,
                };
            }
            return null;
        }

        private Node ParseSum()
        {
            var left = ParseProduct();
            while (IsOperator("+") || IsOperator("-"))
            {
                var op = Current.Text;
                _index++;
                left = new Arithmetic(op, left, ParseProduct());
            }
            return left;
        }

        private Node ParseProduct()
        {
            var left = ParseUnary();
            while (IsOperator("*") || IsOperator("/") || IsOperator("%") || IsKeyword("div") || IsKeyword("mod"))
            {
                var op = Current.Text.ToLowerInvariant() switch
                {
                    "div" => "/",
                    "mod" => "%",
                    var text => text,
                };
                _index++;
                left = new Arithmetic(op, left, ParseUnary());
            }
            return left;
        }

        private Node ParseUnary()
        {
            if (IsOperator("!") || IsKeyword("not"))
            {
                _index++;
                return new Not(ParseUnary());
            }
            if (IsOperator("-"))
            {
                _index++;
                return new Negate(ParseUnary());
            }
            if (IsOperator("+"))
            {
                _index++;
                return ParseUnary();
            }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                case TokenKind.String:
                    _index++;
                    return new Literal(token.Value);
                case TokenKind.Identifier:
                    _index++;
                    var keyword = token.Text.ToLowerInvariant();
                    if (keyword is "true" or "false")
                    {
                        return new Literal(keyword == "true");
                    }
                    if (keyword == "null")
                    {
                        return new Literal(null);
                    }
                    if (IsOperator("("))
                    {
                        throw new ConditionException($"Method or type reference '{token.Text}(' is not allowed");
                    }
                    if (IsReserved(keyword))
                    {
                        throw new ConditionException($"Unexpected keyword '{token.Text}' at position {token.Position}");
                    }
                    return new Variable(token.Text);
                case TokenKind.Operator when token.Text == "(":
                    _index++;
                    var inner = ParseExpression();
                    Expect(")");
                    return inner;
                default:
                    throw new ConditionException(token.Kind == TokenKind.End
                        ? "Unexpected end of expression"
                        : $"Unexpected '{token.Text}' at position {token.Position}");
            }
        }

        private static bool IsReserved(string word) =>
            word is "and" or "or" or "not" or "eq" or "ne" or "lt" or "le" or "gt" or "ge" or "div" or "mod" or "matches";

        private bool IsOperator(string op) => Current.Kind == TokenKind.Operator && Current.Text == op;

        private bool IsKeyword(string word) =>
            Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);

        private void Expect(string op)
        {
            if (!IsOperator(op))
            {
                throw new ConditionException(Current.Kind == TokenKind.End
                    ? $"Expected '{op}' but expression ended"
                    : $"Expected '{op}' at position {Current.Position}");
            }
            _index++;
        }
    }

    // ===== AST =====

    private abstract class Node
    {
        public abstract object? Evaluate(IReadOnlyDictionary<string, object?> vars);

        protected static bool Bool(object? value, string context) =>
            value is bool flag ? flag : throw new ConditionException($"{context} requires true/false, got {Show(value)}");

        protected static decimal Number(object? value, string context) =>
            value is decimal number ? number : throw new ConditionException($"{context} requires a number, got {Show(value)}");
    }

    private sealed class Literal(object? value) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) => value;
    }

    private sealed class Variable(string name) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) =>
            vars.TryGetValue(name, out var value)
                ? value
                : throw new ConditionException($"Property or field '{name}' cannot be found");
    }

    private sealed class Logical(Node left, Node right, bool isAnd) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars)
        {
            var op = isAnd ? "and" : "or";
            var first = Bool(left.Evaluate(vars), op);
            if (isAnd ? !first : first)
            {
                return first;
            }
            return Bool(right.Evaluate(vars), op);
        }
    }

    private sealed class Not(Node operand) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) => !Bool(operand.Evaluate(vars), "not");
    }

    private sealed class Negate(Node operand) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) => -Number(operand.Evaluate(vars), "-");
    }

    private sealed class Ternary(Node condition, Node whenTrue, Node whenFalse) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) =>
            Bool(condition.Evaluate(vars), "?:") ? whenTrue.Evaluate(vars) : whenFalse.Evaluate(vars);
    }

    private sealed class Elvis(Node value, Node fallback) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars) =>
            value.Evaluate(vars) ?? fallback.Evaluate(vars);
    }

    private sealed class Arithmetic(string op, Node left, Node right) : Node
    {
        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars)
        {
            var a = left.Evaluate(vars);
            var b = right.Evaluate(vars);
            if (op == "+" && (a is string || b is string))
            {
                return Text(a) + Text(b);
            }
            var x = Number(a, op);
            var y = Number(b, op);
            try
            {
                return op switch
                {
                    "+" => x + y,
                    "-" => x - y,
                    "*" => x * y,
                    "/" => x / y,
                    _ => x % y,
                };
            }
            catch (Exception ex) when (ex is DivideByZeroException or OverflowException)
            {
                throw new ConditionException(ex.Message);
            }
        }
    }

    private static string Text(object? value) => value as string ?? Show(value);

    private sealed class Relational(string op, Node left, Node right) : Node
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        public override object? Evaluate(IReadOnlyDictionary<string, object?> vars)
        {
            var a = left.Evaluate(vars);
            var b = right.Evaluate(vars);
            switch (op)
            {
                case "==":
                    return Equal(a, b);
                case "!=":
                    return !Equal(a, b);
                case "matches":
                    if (a is not string input || b is not string pattern)
                    {
                        throw new ConditionException("matches requires a string and a regex string");
                    }
                    try
                    {
                        return Regex.IsMatch(input, "^(?:" + pattern + ")$", RegexOptions.None, RegexTimeout);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new ConditionException("Invalid regex: " + ex.Message);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        throw new ConditionException("Regex evaluation timed out");
                    }
                default:
                    var comparison = Compare(a, b);
                    return op switch
                    {
                        "<" => comparison < 0,
                        "<=" => comparison <= 0,
                        ">" => comparison > 0,
                        _ => comparison >= 0,
                    };
            }
        }

        private static bool Equal(object? a, object? b) => (a, b) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (decimal x, decimal y) => x == y,
            (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
            (bool x, bool y) => x == y,
            _ => false,
        };

        /// <summary>Như StandardTypeComparator của SpEL: null nhỏ hơn mọi giá trị; khác kiểu → lỗi.</summary>
        private int Compare(object? a, object? b) => (a, b) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (decimal x, decimal y) => x.CompareTo(y),
            (string x, string y) => string.CompareOrdinal(x, y),
            (bool x, bool y) => x.CompareTo(y),
            _ => throw new ConditionException($"Cannot compare {Show(a)} {op} {Show(b)}"),
        };
    }
}
