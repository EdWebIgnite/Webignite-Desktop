using System.Globalization;
using Webignite.Core.Documents;

namespace Webignite.Core.Tools;

/// <summary>The answer to a sum, or why it couldn't be worked out.</summary>
public sealed record CalcResult(decimal? Value, string? Problem)
{
    public bool Ok => Value is not null;
}

/// <summary>
/// A small calculator for money sums, typed the way people write them: "3.5 x 650", "R1 200 + 15%",
/// "(2 400 - 300) / 12". Works in decimal so cents never drift. "+ 15%" adds 15% of what came before,
/// like a desk calculator; "15%" on its own is 0.15.
/// </summary>
public static class Calculator
{
    public static CalcResult Evaluate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(null, "Type a sum.");
        try
        {
            var parser = new Parser(Tokens(text));
            var value = parser.Sum();
            if (!parser.AtEnd)
                return new(null, "Check the sum: something after the end doesn't fit.");
            return new(value.Number, null);
        }
        catch (DivideByZeroException)
        {
            return new(null, "Can't divide by zero.");
        }
        catch (OverflowException)
        {
            return new(null, "That number is too big.");
        }
        catch (FormatException e)
        {
            return new(null, e.Message);
        }
    }

    /// <summary>An amount typed in a price or quantity box: a plain number, or a sum like "3 x 650".</summary>
    public static decimal? Amount(string? text) =>
        Money.Parse(text) ?? (LooksLikeSum(text) && Evaluate(text) is { Ok: true } r ? r.Value : null);

    /// <summary>Whether the text has an operator in it, so it's worth working out.</summary>
    public static bool LooksLikeSum(string? text) =>
        text is not null && text.Trim().Skip(1).Any(c => c is '+' or '-' or '*' or '/' or 'x' or 'X' or '×' or '÷' or '%' or '(' or ')');

    /// <summary>A result to put back in a box: no thousands spaces, at most 4 decimals, no trailing zeros.</summary>
    public static string Plain(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    private enum Kind { Number, Op, Open, Close, Percent }

    private readonly record struct Token(Kind Kind, decimal Number = 0, char Op = ' ');

    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var s = text.Replace(' ', ' ').Trim();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c) || c is 'R' or 'r')
            {
                i++;
                continue;
            }
            if (char.IsDigit(c) || c is '.' or ',')
            {
                var start = i;
                // A number may have spaces between groups of three: "1 250 000".
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or ',' ||
                       (s[i] == ' ' && i + 3 < s.Length + 1 && i + 1 < s.Length && char.IsDigit(s[i + 1]) && GroupOfThree(s, i + 1))))
                    i++;
                var number = Money.Parse(s[start..i]) ?? throw new FormatException($"\"{s[start..i].Trim()}\" isn't a number.");
                tokens.Add(new Token(Kind.Number, number));
                continue;
            }
            switch (c)
            {
                case '+' or '-':
                    tokens.Add(new Token(Kind.Op, Op: c));
                    break;
                case '*' or 'x' or 'X' or '×':
                    tokens.Add(new Token(Kind.Op, Op: '*'));
                    break;
                case '/' or '÷':
                    tokens.Add(new Token(Kind.Op, Op: '/'));
                    break;
                case '(':
                    tokens.Add(new Token(Kind.Open));
                    break;
                case ')':
                    tokens.Add(new Token(Kind.Close));
                    break;
                case '%':
                    tokens.Add(new Token(Kind.Percent));
                    break;
                default:
                    throw new FormatException($"\"{c}\" can't be used in a sum.");
            }
            i++;
        }
        return tokens;
    }

    // "1 250" is one number, but "1 25" or "12 3456" are not grouped thousands.
    private static bool GroupOfThree(string s, int at)
    {
        var digits = 0;
        while (at + digits < s.Length && char.IsDigit(s[at + digits]))
            digits++;
        return digits == 3;
    }

    /// <summary>A value, and whether it was written as a percentage, so "+ 15%" can mean 15% of the left side.</summary>
    private readonly record struct Value(decimal Number, bool IsPercent = false);

    private sealed class Parser(List<Token> tokens)
    {
        private int at;

        public bool AtEnd => at >= tokens.Count;

        private Token? Peek => at < tokens.Count ? tokens[at] : null;

        public Value Sum()
        {
            var left = Product();
            while (Peek is { Kind: Kind.Op, Op: '+' or '-' } op)
            {
                at++;
                var right = Product();
                var amount = right.IsPercent ? left.Number * right.Number : right.Number;
                left = new Value(op.Op == '+' ? left.Number + amount : left.Number - amount);
            }
            return left;
        }

        private Value Product()
        {
            var left = Unary();
            while (Peek is { Kind: Kind.Op, Op: '*' or '/' } op)
            {
                at++;
                var right = Unary();
                left = new Value(op.Op == '*' ? left.Number * right.Number : left.Number / right.Number);
            }
            return left;
        }

        private Value Unary()
        {
            if (Peek is { Kind: Kind.Op, Op: '-' })
            {
                at++;
                var inner = Unary();
                return inner with { Number = -inner.Number };
            }
            if (Peek is { Kind: Kind.Op, Op: '+' })
                at++;
            return Postfix();
        }

        private Value Postfix()
        {
            var value = Atom();
            if (Peek is { Kind: Kind.Percent })
            {
                at++;
                value = new Value(value.Number / 100m, IsPercent: true);
            }
            return value;
        }

        private Value Atom()
        {
            switch (Peek)
            {
                case { Kind: Kind.Number } number:
                    at++;
                    return new Value(number.Number);
                case { Kind: Kind.Open }:
                    at++;
                    var inner = Sum();
                    if (Peek is not { Kind: Kind.Close })
                        throw new FormatException("A bracket isn't closed.");
                    at++;
                    return new Value(inner.Number);
                case null:
                    throw new FormatException("The sum stops too early.");
                default:
                    throw new FormatException("Check the sum: two signs are next to each other.");
            }
        }
    }
}
