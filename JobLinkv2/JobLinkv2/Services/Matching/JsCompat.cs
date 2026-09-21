using System.Globalization;
using System.Text.Json;

namespace JobLinkv2.Services.Matching
{
    // The few places where the C# scorer must do what the browser's JavaScript did, so the same job
    // and the same job seeker give the same number on both (docs/scoring.md is the written rules,
    // tests/golden the proof). Nothing else in the app needs this.
    public static class JsCompat
    {
        // Math.round: the nearest whole number, halves going UP (12.5 -> 13, -2.5 -> -2).
        // Math.Round would go to the nearest EVEN number (12.5 -> 12), which would change scores.
        public static double Round(double value)
        {
            var floor = Math.Floor(value);

            return value - floor >= 0.5 ? floor + 1 : floor;
        }

        // Number(x) || 0: what the browser did to every salary. Anything that isn't a usable
        // number (missing, null, "", "abc", NaN) is 0, and so is a real 0 - "not given".
        public static double NumberOrZero(double value) =>
            double.IsNaN(value) || value == 0 ? 0 : value;

        // Number(value) for a value read from JSON: numbers as they are, numeric strings the way
        // JavaScript reads them (surrounding spaces ignored, "" is 0, "2.5e4", "0x10", "Infinity"),
        // true is 1, null and false are 0, anything else is NaN. (JS also turns [] into 0 and [5]
        // into 5; JSearch never sends that, so an array or object is NaN here.)
        public static double ToNumber(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String => ToNumber(value.GetString()),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Null => 0,
            _ => double.NaN
        };

        public static double ToNumber(string? text)
        {
            var trimmed = Trim(text ?? "");

            if (trimmed.Length == 0)
                return 0;

            if (trimmed.Length > 2 && trimmed[0] == '0' && ParseRadix(trimmed) is double radix)
                return radix;

            switch (trimmed)
            {
                case "Infinity":
                case "+Infinity":
                    return double.PositiveInfinity;
                case "-Infinity":
                    return double.NegativeInfinity;
            }

            return IsDecimalLiteral(trimmed) && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : double.NaN;
        }

        // 0x1F, 0o17, 0b101 (no sign allowed, as in JavaScript).
        private static double? ParseRadix(string text)
        {
            var radix = text[1] switch { 'x' or 'X' => 16, 'o' or 'O' => 8, 'b' or 'B' => 2, _ => 0 };

            if (radix == 0)
                return null;

            double result = 0;

            foreach (var ch in text.AsSpan(2))
            {
                var digit = ch switch { >= '0' and <= '9' => ch - '0', >= 'a' and <= 'f' => ch - 'a' + 10, >= 'A' and <= 'F' => ch - 'A' + 10, _ => 99 };

                if (digit >= radix)
                    return double.NaN;

                result = result * radix + digit;
            }

            return result;
        }

        // [+-] digits [. digits] [e [+-] digits], or [+-] . digits ... - what StrDecimalLiteral allows.
        // (double.TryParse alone would also accept things JavaScript does not, like "1,000" or "1_0".)
        private static bool IsDecimalLiteral(string text)
        {
            var i = 0;

            if (text[i] is '+' or '-')
                i++;

            var digits = 0;

            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }

            if (i < text.Length && text[i] == '.')
            {
                i++;

                while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
            }

            if (digits == 0)
                return false;

            if (i < text.Length && text[i] is 'e' or 'E')
            {
                i++;

                if (i < text.Length && text[i] is '+' or '-')
                    i++;

                var exponent = 0;

                while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; exponent++; }

                if (exponent == 0)
                    return false;
            }

            return i == text.Length;
        }

        // The characters String.prototype.trim removes (ECMAScript WhiteSpace and LineTerminator).
        // char.IsWhiteSpace differs: it also trims U+0085 and misses U+FEFF.
        public static bool IsWhitespace(char ch) =>
            (int)ch is 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 or 0xA0 or 0x1680 or (>= 0x2000 and <= 0x200A)
                or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 or 0xFEFF;

        public static string Trim(string text)
        {
            var start = 0;
            var end = text.Length;

            while (start < end && IsWhitespace(text[start])) start++;
            while (end > start && IsWhitespace(text[end - 1])) end--;

            return text[start..end];
        }

        // toLowerCase() on text that will be compared.
        public static string Lower(string text) => text.ToLowerInvariant();
    }
}
