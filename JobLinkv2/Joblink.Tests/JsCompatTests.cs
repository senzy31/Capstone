using System.Text.Json;
using JobLinkv2.Services.Matching;
using Xunit;

namespace Joblink.Tests
{
    // The small pieces that make the C# scorer round and read numbers like the browser did. The expected values
    // were taken from JavaScript itself (Number(...), String.prototype.trim), not worked out.
    public class JsCompatTests
    {
        private static void SameNumber(double expected, double actual)
        {
            if (double.IsNaN(expected))
                Assert.True(double.IsNaN(actual), $"expected NaN, got {actual}");
            else
                Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(0.5, 1)]
        [InlineData(1.5, 2)]
        [InlineData(2.5, 3)]        // Math.Round would give 2
        [InlineData(12.5, 13)]
        [InlineData(62.5, 63)]
        [InlineData(0.4999, 0)]
        [InlineData(-0.5, 0)]
        [InlineData(-1.5, -1)]
        [InlineData(-2.5, -2)]
        [InlineData(-16.67, -17)]
        [InlineData(83.33333333333334, 83)]
        [InlineData(0.49999999999999994, 0)]      // the value a floor(x + 0.5) implementation gets wrong
        [InlineData(4503599627370495.5, 4503599627370496)]
        public void Round_sends_halves_up_like_Math_round(double value, double expected)
        {
            Assert.Equal(expected, JsCompat.Round(value));
        }

        [Fact]
        public void Round_leaves_NaN_and_infinity_alone()
        {
            Assert.True(double.IsNaN(JsCompat.Round(double.NaN)));
            Assert.Equal(double.PositiveInfinity, JsCompat.Round(double.PositiveInfinity));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(40000, 40000)]
        [InlineData(-5, -5)]
        [InlineData(double.NaN, 0)]
        public void Number_or_zero_treats_anything_unusable_as_not_given(double value, double expected)
        {
            Assert.Equal(expected, JsCompat.NumberOrZero(value));
        }

        [Theory]
        [InlineData("  42  ", 42)]
        [InlineData("4e2", 400)]
        [InlineData("1E-2", 0.01)]
        [InlineData("1e3", 1000)]
        [InlineData("00012", 12)]
        [InlineData("0.0", 0)]
        [InlineData("+.5e1", 5)]
        [InlineData("+5", 5)]
        [InlineData("-5", -5)]
        [InlineData(".5", 0.5)]
        [InlineData("5.", 5)]
        [InlineData("0x10", 16)]
        [InlineData("0X1f", 31)]
        [InlineData("0b11", 3)]
        [InlineData("0o17", 15)]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("Infinity", double.PositiveInfinity)]
        [InlineData("+Infinity", double.PositiveInfinity)]
        [InlineData("-Infinity", double.NegativeInfinity)]
        [InlineData("1e400", double.PositiveInfinity)]
        [InlineData("abc", double.NaN)]
        [InlineData("12abc", double.NaN)]
        [InlineData("1,000", double.NaN)]
        [InlineData("1_0", double.NaN)]
        [InlineData("1 2", double.NaN)]
        [InlineData(".", double.NaN)]
        [InlineData("e5", double.NaN)]
        [InlineData("1e", double.NaN)]
        [InlineData("1e+", double.NaN)]
        [InlineData("infinity", double.NaN)]
        [InlineData("+0x10", double.NaN)]
        [InlineData("0x", double.NaN)]
        [InlineData("0xg", double.NaN)]
        [InlineData("0b12", double.NaN)]
        [InlineData("0o8", double.NaN)]
        [InlineData("--5", double.NaN)]
        [InlineData("+-5", double.NaN)]
        public void A_string_is_read_as_a_number_the_way_JavaScript_reads_it(string text, double expected)
        {
            SameNumber(expected, JsCompat.ToNumber(text));
        }

        [Fact]
        public void Negative_zero_reads_as_zero_in_effect()
        {
            Assert.Equal(0, JsCompat.NumberOrZero(JsCompat.ToNumber("-0")));
        }

        [Fact]
        public void Spaces_of_every_kind_JavaScript_trims_are_ignored_around_a_number()
        {
            foreach (var codePoint in new[] { 0xA0, 0xFEFF, 0x2028, 0x2029, 0x2003, 0x3000 })
                Assert.Equal(42, JsCompat.ToNumber(char.ConvertFromUtf32(codePoint) + "42" + char.ConvertFromUtf32(codePoint)));

            foreach (var codePoint in new[] { 0x85, 0x180E, 0x200B })      // JavaScript does not treat these as spaces
                Assert.True(double.IsNaN(JsCompat.ToNumber(char.ConvertFromUtf32(codePoint) + "42")));
        }

        [Theory]
        [InlineData("12345", 12345.0)]
        [InlineData("\"12345\"", 12345.0)]
        [InlineData("true", 1.0)]
        [InlineData("false", 0.0)]
        [InlineData("null", 0.0)]
        [InlineData("[]", double.NaN)]
        [InlineData("{}", double.NaN)]
        [InlineData("-3.5", -3.5)]
        public void A_json_value_is_read_as_a_number_the_way_JavaScript_reads_it(string json, double expected)
        {
            using var document = JsonDocument.Parse(json);

            SameNumber(expected, JsCompat.ToNumber(document.RootElement));
        }

        // What String.prototype.trim removes: exactly these 25 characters (checked in Node over all 65,536).
        [Fact]
        public void Whitespace_is_exactly_what_JavaScript_trims()
        {
            var trimmed = new HashSet<int> { 0x9, 0xA, 0xB, 0xC, 0xD, 0x20, 0xA0, 0x1680, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF };

            for (var c = 0x2000; c <= 0x200A; c++)
                trimmed.Add(c);

            for (var c = 0; c <= 0xFFFF; c++)
                Assert.True(JsCompat.IsWhitespace((char)c) == trimmed.Contains(c), $"U+{c:X4}");
        }

        [Fact]
        public void Trim_removes_JavaScript_whitespace_from_both_ends_only()
        {
            var nbsp = char.ConvertFromUtf32(0xA0);
            var bom = char.ConvertFromUtf32(0xFEFF);

            Assert.Equal("a b", JsCompat.Trim($"{nbsp} \t{bom}a b\r\n{nbsp}"));
            Assert.Equal("", JsCompat.Trim(" \t "));
            Assert.Equal("", JsCompat.Trim(""));
            Assert.Equal(char.ConvertFromUtf32(0x85) + "x", JsCompat.Trim(char.ConvertFromUtf32(0x85) + "x"));    // U+0085 is not JS whitespace
        }
    }
}
