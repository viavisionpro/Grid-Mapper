using System.Numerics;

namespace GridMapper.Core;

public sealed class NumericExpressionException(string message) : FormatException(message);

public static class NumericExpression
{
    public const int MaxLength = 256;
    public const int MaxDepth = 32;
    public const int MaxTokens = 128;
    public const int MaxLiteralDigits = 28;
    private const int MaxBits = 4096;
    private static readonly BigInteger DecimalMaximum = new(decimal.MaxValue);
    private static readonly BigInteger FractionScale = BigInteger.Pow(10, 28);

    public static decimal Evaluate(string? expression)
    {
        var value = new Parser(expression).Parse();
        var numerator = BigInteger.Abs(value.Numerator);
        if (numerator > DecimalMaximum * value.Denominator)
            throw new NumericExpressionException("Результат выражения слишком велик.");
        var whole = BigInteger.DivRem(numerator, value.Denominator, out var remainder);
        // Retain 28 decimal places without converting a potentially large denominator.
        var fraction = remainder * FractionScale / value.Denominator;
        var result = (decimal)whole + (decimal)fraction / 10_000_000_000_000_000_000_000_000_000m;
        return value.Numerator.Sign < 0 ? -result : result;
    }

    public static int EvaluateInt32(string? expression)
    {
        var value = new Parser(expression).Parse();
        var whole = BigInteger.DivRem(value.Numerator, value.Denominator, out var remainder);
        if (!remainder.IsZero) throw new NumericExpressionException("Результат должен быть целым числом.");
        if (whole < int.MinValue || whole > int.MaxValue)
            throw new NumericExpressionException("Целый результат выходит за допустимый диапазон.");
        return (int)whole;
    }

    private readonly record struct Fraction(BigInteger Numerator, BigInteger Denominator)
    {
        public static Fraction Create(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new NumericExpressionException("Деление на ноль недопустимо.");
            if (numerator.GetBitLength() > MaxBits || denominator.GetBitLength() > MaxBits)
                throw new NumericExpressionException("Выражение слишком сложное.");
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            return new(numerator / divisor, denominator / divisor);
        }
        public static Fraction operator +(Fraction a, Fraction b) => Create(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Fraction operator -(Fraction a, Fraction b) => Create(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Fraction operator *(Fraction a, Fraction b) => Create(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public static Fraction operator /(Fraction a, Fraction b) => Create(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;
        private int _depth;
        private int _tokens;
        public Parser(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new NumericExpressionException("Введите число или выражение.");
            if (text.Length > MaxLength) throw new NumericExpressionException($"Выражение длиннее {MaxLength} символов.");
            _text = text;
        }
        public Fraction Parse()
        {
            var result = Sum(); SkipSpace();
            if (_position != _text.Length) throw Syntax();
            return result;
        }
        private Fraction Sum()
        {
            var result = Product();
            while (true)
            {
                if (Take('+')) result += Product();
                else if (Take('-') || Take('−')) result -= Product();
                else return result;
            }
        }
        private Fraction Product()
        {
            var result = Factor();
            while (true)
            {
                if (Take('*') || Take('×')) result *= Factor();
                else if (Take('/')) result /= Factor();
                else return result;
            }
        }
        private Fraction Factor()
        {
            if (++_depth > MaxDepth) throw new NumericExpressionException($"Допускается не более {MaxDepth} уровней скобок и знаков.");
            try
            {
                if (Take('+')) return Factor();
                if (Take('-') || Take('−')) { var value = Factor(); return new(-value.Numerator, value.Denominator); }
                if (Take('('))
                {
                    var value = Sum();
                    if (!Take(')')) throw new NumericExpressionException("Закройте скобку в выражении.");
                    return value;
                }
                return Number();
            }
            finally { _depth--; }
        }
        private Fraction Number()
        {
            SkipSpace(); CountToken();
            var numerator = BigInteger.Zero;
            var digits = 0; var fractionalDigits = 0; var dot = false;
            while (_position < _text.Length)
            {
                var c = _text[_position];
                if (c == '.' && !dot) { dot = true; _position++; continue; }
                if (c is < '0' or > '9') break;
                if (++digits > MaxLiteralDigits) throw new NumericExpressionException($"Число может содержать до {MaxLiteralDigits} цифр.");
                numerator = numerator * 10 + (c - '0');
                if (dot) fractionalDigits++;
                _position++;
            }
            if (digits == 0) throw Syntax();
            return Fraction.Create(numerator, BigInteger.Pow(10, fractionalDigits));
        }
        private bool Take(char c)
        {
            SkipSpace();
            if (_position >= _text.Length || _text[_position] != c) return false;
            _position++; CountToken(); return true;
        }
        private void CountToken()
        {
            if (++_tokens > MaxTokens) throw new NumericExpressionException("В выражении слишком много чисел и знаков.");
        }
        private void SkipSpace() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
        private NumericExpressionException Syntax() => new($"Проверьте выражение около символа {_position + 1}. Допустимы числа, +, −, ×, / и скобки.");
    }
}
