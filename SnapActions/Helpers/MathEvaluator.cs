namespace SnapActions.Helpers;

/// <summary>
/// Simple recursive descent math expression parser.
/// Supports: +, -, *, /, %, ^, parentheses, unary minus.
/// </summary>
public static class MathEvaluator
{
    public static double Evaluate(string expression)
    {
        var parser = new Parser(expression.Replace(" ", ""));
        var result = parser.ParseExpression();
        if (parser.Position < parser.Input.Length)
            throw new FormatException($"Unexpected character: {parser.Input[parser.Position]}");
        return result;
    }

    private class Parser(string input)
    {
        public string Input { get; } = input;
        public int Position { get; private set; }

        public double ParseExpression()
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
                Position++; // skip '('
                var result = ParseExpression();
                if (Position < Input.Length && Input[Position] == ')')
                    Position++; // skip ')'
                return result;
            }

            // Parse number
            int start = Position;
            while (Position < Input.Length && (char.IsDigit(Input[Position]) || Input[Position] == '.'))
                Position++;

            if (start == Position)
                throw new FormatException($"Expected number at position {Position}");

            return double.Parse(Input[start..Position], System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
