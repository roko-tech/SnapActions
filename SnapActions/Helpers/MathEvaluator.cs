namespace SnapActions.Helpers;

/// <summary>
/// Recursive-descent math expression parser.
/// Operators: + - * / % ^ and parentheses, unary +/-.
/// Functions: sqrt, sin, cos, tan, log (natural), log10, abs, round, floor, ceil, exp.
/// Constants: pi, e.
/// Commas are stripped (so "1,000+200" works).
/// </summary>
public static class MathEvaluator
{
    public static double Evaluate(string expression)
    {
        var cleaned = expression.Replace(" ", "").Replace(",", "");
        var parser = new Parser(cleaned);
        var result = parser.ParseExpression();
        if (parser.Position < parser.Input.Length)
            throw new FormatException($"Unexpected character: {parser.Input[parser.Position]}");
        return result;
    }

    private class Parser(string input)
    {
        public string Input { get; } = input;
        public int Position { get; private set; }

        // Cap the recursion depth so a pathological input like 200 nested "(" can't blow the
        // managed stack. 64 is comfortably more than any realistic expression a user would type.
        private const int MaxDepth = 64;
        private int _depth;

        public double ParseExpression()
        {
            if (++_depth > MaxDepth)
                throw new FormatException("Expression nesting too deep");
            try
            {
                var left = ParseTerm();
                while (Position < Input.Length && (Input[Position] == '+' || Input[Position] == '-'))
                {
                    char op = Input[Position++];
                    var right = ParseTerm();
                    left = op == '+' ? left + right : left - right;
                }
                return left;
            }
            finally { _depth--; }
        }

        private double ParseTerm()
        {
            var left = ParsePower();
            while (Position < Input.Length && (Input[Position] == '*' || Input[Position] == '/' || Input[Position] == '%'))
            {
                char op = Input[Position++];
                var right = ParsePower();
                left = op switch
                {
                    '*' => left * right,
                    '/' => right != 0 ? left / right : throw new DivideByZeroException(),
                    '%' => left % right,
                    _ => left
                };
            }
            return left;
        }

        private double ParsePower()
        {
            var baseVal = ParseUnary();
            if (Position < Input.Length && Input[Position] == '^')
            {
                Position++;
                var exp = ParseUnary();
                return Math.Pow(baseVal, exp);
            }
            return baseVal;
        }

        private double ParseUnary()
        {
            if (Position < Input.Length && Input[Position] == '-')
            {
                Position++;
                return -ParsePrimary();
            }
            if (Position < Input.Length && Input[Position] == '+')
            {
                Position++;
            }
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Position < Input.Length && Input[Position] == '(')
            {
                Position++;
                var result = ParseExpression();
                if (Position >= Input.Length || Input[Position] != ')')
                    throw new FormatException($"Missing ')' at position {Position}");
                Position++;
                return result;
            }

            // Identifier (function or constant)
            if (Position < Input.Length && IsLetter(Input[Position]))
            {
                int start = Position;
                while (Position < Input.Length && IsLetter(Input[Position])) Position++;
                var name = Input[start..Position].ToLowerInvariant();

                if (Position < Input.Length && Input[Position] == '(')
                {
                    Position++;
                    var arg = ParseExpression();
                    if (Position >= Input.Length || Input[Position] != ')')
                        throw new FormatException($"Missing ')' for {name}() at position {Position}");
                    Position++;
                    return ApplyFunction(name, arg);
                }
                return ApplyConstant(name);
            }

            // Number
            int numStart = Position;
            while (Position < Input.Length && (char.IsDigit(Input[Position]) || Input[Position] == '.'))
                Position++;

            if (numStart == Position)
                throw new FormatException($"Expected number at position {Position}");

            return double.Parse(Input[numStart..Position], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static double ApplyFunction(string name, double arg) => name switch
        {
            "sqrt" => Math.Sqrt(arg),
            "sin" => Math.Sin(arg),
            "cos" => Math.Cos(arg),
            "tan" => Math.Tan(arg),
            "log" => Math.Log(arg),
            "ln" => Math.Log(arg),
            "log10" => Math.Log10(arg),
            "log2" => Math.Log2(arg),
            "abs" => Math.Abs(arg),
            "round" => Math.Round(arg, MidpointRounding.AwayFromZero),
            "floor" => Math.Floor(arg),
            "ceil" => Math.Ceiling(arg),
            "exp" => Math.Exp(arg),
            _ => throw new FormatException($"Unknown function: {name}")
        };

        private static double ApplyConstant(string name) => name switch
        {
            "pi" => Math.PI,
            "e" => Math.E,
            "tau" => Math.Tau,
            _ => throw new FormatException($"Unknown identifier: {name}")
        };
    }
}
