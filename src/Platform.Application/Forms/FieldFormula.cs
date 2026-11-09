using System.Globalization;

namespace Platform.Application.Forms;

/// <summary>
/// Calculated number fields (added 2026-11-10): a small arithmetic expression over sibling
/// number fields and constants - <c>+ - * /</c>, parentheses, unary minus. Field codes are the
/// only names allowed. The server computes the value on every save and stores it; the browser
/// shows the same result live (platform-web <c>forms/formula.ts</c> must agree with this).
/// A missing input, or a division by zero, gives no value (null) rather than an error.
/// </summary>
public static class FieldFormula
{
    /// <summary>The field codes the formula reads; throws FormatException for a malformed formula.</summary>
    public static IReadOnlyList<string> References(string formula)
    {
        var parser = new Parser(Tokenize(formula), _ => 0m);
        parser.ParseAll();
        return parser.Names.Distinct().ToList();
    }

    public static decimal? Evaluate(string formula, Func<string, decimal?> valueOf)
    {
        try
        {
            var result = new Parser(Tokenize(formula), code => valueOf(code) ?? throw new MissingValue()).ParseAll();
            return decimal.Round(result, 4, MidpointRounding.AwayFromZero);
        }
        catch (MissingValue) { return null; }
        catch (DivideByZeroException) { return null; }
        catch (OverflowException) { return null; }
    }

    private sealed class MissingValue : Exception;

    private readonly record struct Token(char Kind, string Text); // n=number, i=identifier, or the operator char

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add(new Token('n', text[start..i]));
                continue;
            }
            if (char.IsAsciiLetterLower(c))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterLower(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new Token('i', text[start..i]));
                continue;
            }
            if ("+-*/()".Contains(c)) { tokens.Add(new Token(c, c.ToString())); i++; continue; }
            throw new FormatException($"Unexpected '{c}' in the formula.");
        }
        if (tokens.Count == 0) throw new FormatException("The formula is empty.");
        return tokens;
    }

    private sealed class Parser(List<Token> tokens, Func<string, decimal> valueOf)
    {
        private int _pos;
        public List<string> Names { get; } = [];

        public decimal ParseAll()
        {
            var value = Expression();
            if (_pos != tokens.Count) throw new FormatException("The formula has something extra at the end.");
            return value;
        }

        private decimal Expression()
        {
            var value = Term();
            while (Peek is '+' or '-')
            {
                var op = tokens[_pos++].Kind;
                var right = Term();
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private decimal Term()
        {
            var value = Factor();
            while (Peek is '*' or '/')
            {
                var op = tokens[_pos++].Kind;
                var right = Factor();
                value = op == '*' ? value * right : value / right;
            }
            return value;
        }

        private decimal Factor()
        {
            if (_pos >= tokens.Count) throw new FormatException("The formula ends too early.");
            var token = tokens[_pos++];
            switch (token.Kind)
            {
                case '-': return -Factor();
                case '(':
                    var inner = Expression();
                    if (Peek != ')') throw new FormatException("A bracket isn't closed.");
                    _pos++;
                    return inner;
                case 'n':
                    return decimal.TryParse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                        ? number : throw new FormatException($"'{token.Text}' isn't a number.");
                case 'i':
                    Names.Add(token.Text);
                    return valueOf(token.Text);
                default:
                    throw new FormatException($"Unexpected '{token.Text}' in the formula.");
            }
        }

        private char? Peek => _pos < tokens.Count ? tokens[_pos].Kind : null;
    }
}
