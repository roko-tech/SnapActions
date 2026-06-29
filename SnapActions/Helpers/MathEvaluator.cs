namespace SnapActions.Helpers;

/// <summary>
/// Recursive-descent math expression parser.
/// Operators: + - * / % ^ and parentheses, unary +/-.
/// Functions: sqrt, sin, cos, tan, log (natural), ln, log10, log2, abs, round, floor, ceil, exp.
/// Constants: pi, e, tau.
/// Numbers use period as the decimal separator. Commas are treated as thousands separators and
/// stripped before parsing (so "1,000+200" works). European-decimal numbers like "1,5*2" will
/// be misparsed as "15*2"; users on European locales should write the decimal as "1.5".
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
            // Guard the same shared depth counter ParseExpression uses. The '^' right-recursion
            // below is a second unbounded descent path — without this, a selection like
            // "2^2^2^...^2" (thousands of carets) recurses here to stack exhaustion and throws an
            // *uncatchable* StackOverflowException that crashes the whole process. The depth cap
            // turns that into a catchable FormatException instead.
            if (++_depth > MaxDepth)
                throw new FormatException("Expression nesting too deep");
            try
            {
                var baseVal = ParseUnary();
                if (Position < Input.Length && Input[Position] == '^')
                {
                    Position++;
                    // Right-associative: 2^3^2 evaluates as 2^(3^2) = 512, matching standard math
                    // convention. Recursing into ParsePower instead of ParseUnary makes that work.
                    var exp = ParsePower();
                    return Math.Pow(baseVal, exp);
                }
                return baseVal;
            }
            finally { _depth--; }
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

            // Number — including scientific notation (1e10, 2.5e-3). Without the exponent loop,
            // a selection like "1e10" used to parse "1" then identifier "e" then leftover "10",
            // throwing "Unexpected character".
            int numStart = Position;
            while (Position < Input.Length && (char.IsDigit(Input[Position]) || Input[Position] == '.'))
                Position++;

            if (numStart < Position && Position < Input.Length &&
                (Input[Position] == 'e' || Input[Position] == 'E'))
            {
                int exponentStart = Position;
                Position++;
                if (Position < Input.Length && (Input[Position] == '+' || Input[Position] == '-'))
                    Position++;
                int expDigitStart = Position;
                while (Position < Input.Length && char.IsDigit(Input[Position]))
                    Position++;
                // Roll back if the exponent had no digits — could legitimately be "2*e" rather
                // than scientific notation. The constant lookup below will pick "e" up.
                if (Position == expDigitStart) Position = exponentStart;
            }

            if (numStart == Position)
                throw new FormatException($"Expected number at position {Position}");

            return double.Parse(Input[numStart..Position],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);
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
