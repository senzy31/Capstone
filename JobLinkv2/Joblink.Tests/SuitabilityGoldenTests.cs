using System.Text.Json;
using JobLinkv2.Services.Matching;
using Xunit;

namespace Joblink.Tests
{
    // The C# scorer against the browser scorer it replaced. tests/golden/suitability.golden.json holds
    // 600+ jobs and job seekers and what the ORIGINAL JavaScript answered for each (score, band, the three
    // parts and their notes); every one must come out exactly the same here. The file is produced by
    // tests/golden/generate.js from a frozen copy of that JavaScript, and `npm test` checks it still is.
    public class SuitabilityGoldenTests
    {
        private static readonly string[] SavedArrangements = { "onsite", "remote", "hybrid" };

        private static JsonElement Load(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "golden", name);

            Assert.True(File.Exists(path), $"{path} is missing - is tests/golden copied to the test output?");

            using var document = JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.Clone();
        }

        private static string? Str(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static double Num(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) ? JsCompat.NumberOrZero(JsCompat.ToNumber(value)) : 0;

        private static ScoringProfile ProfileOf(JsonElement profile)
        {
            var skills = profile.TryGetProperty("skills", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(skill => skill.GetString()!).ToList()
                : new List<string>();

            ScoringPreferences? preferences = null;

            if (profile.TryGetProperty("preferences", out var prefs) && prefs.ValueKind == JsonValueKind.Object)
                preferences = new ScoringPreferences(Str(prefs, "preferredLocation"), Str(prefs, "workArrangement"), Num(prefs, "minSalary"), Num(prefs, "maxSalary"));

            return new ScoringProfile(skills, preferences);
        }

        private static string? Difference(JsonElement golden)
        {
            var job = ScoringJob.FromJson(golden.GetProperty("job"));
            var profile = ProfileOf(golden.GetProperty("profile"));
            var expected = golden.GetProperty("expected");

            var actual = SuitabilityScorer.Score(job, profile);

            var problems = new List<string>();

            void Check(string what, object? want, object? got)
            {
                if (!Equals(want, got))
                    problems.Add($"{what}: browser {want ?? "null"}, C# {got ?? "null"}");
            }

            Check("score", expected.GetProperty("score").GetInt32(), actual.Score);
            Check("band level", expected.GetProperty("band").GetProperty("level").GetString(), actual.Band.Level);
            Check("band label", expected.GetProperty("band").GetProperty("label").GetString(), actual.Band.Label);

            var skills = expected.GetProperty("skills");

            if (skills.ValueKind == JsonValueKind.Null)
                Check("skills part", null, actual.Skills);
            else if (actual.Skills is null)
                Check("skills part", "scored", null);
            else
            {
                Check("skills score", skills.GetProperty("score").GetInt32(), actual.Skills.Score);
                Check("skills total", skills.GetProperty("total").GetInt32(), actual.Skills.Total);
                Check("skills matched", string.Join("|", skills.GetProperty("matched").EnumerateArray().Select(m => m.GetString())), string.Join("|", actual.Skills.Matched));
            }

            // The browser named an unrecognised work arrangement "undefined" in its notes (the preferences page only ever
            // saves onsite / remote / hybrid, so a user never sees it); the server says the value instead.
            var arrangement = profile.Preferences?.WorkArrangement ?? "";
            var unsaved = arrangement.Length > 0 && !SavedArrangements.Contains(arrangement);

            CheckPart("location", expected.GetProperty("location"), actual.Location, unsaved ? arrangement : null);
            CheckPart("salary", expected.GetProperty("salary"), actual.Salary, null);

            void CheckPart(string name, JsonElement want, ScorePart? got, string? replaceUndefinedWith)
            {
                if (want.ValueKind == JsonValueKind.Null)
                {
                    Check($"{name} part", null, got);
                    return;
                }

                if (got is null)
                {
                    Check($"{name} part", "scored", null);
                    return;
                }

                var note = want.GetProperty("note").GetString()!;

                if (replaceUndefinedWith != null)
                    note = note.Replace("undefined", replaceUndefinedWith);

                Check($"{name} score", want.GetProperty("score").GetInt32(), got.Score);
                Check($"{name} note", note, got.Note);
            }

            return problems.Count == 0 ? null : $"{golden.GetProperty("name").GetString()}\n      " + string.Join("\n      ", problems);
        }

        [Fact]
        public void The_golden_file_is_there_and_big_enough_to_mean_something()
        {
            var golden = Load("suitability.golden.json");

            Assert.True(golden.GetProperty("cases").GetArrayLength() >= 600);
            Assert.Equal(golden.GetProperty("count").GetInt32(), golden.GetProperty("cases").GetArrayLength());
        }

        [Fact]
        public void Every_golden_case_scores_exactly_as_the_browser_did()
        {
            var golden = Load("suitability.golden.json");

            var differences = golden.GetProperty("cases").EnumerateArray()
                .Select(Difference)
                .Where(difference => difference != null)
                .ToList();

            Assert.True(differences.Count == 0,
                $"{differences.Count} of {golden.GetProperty("count").GetInt32()} golden cases differ from the browser's answer:\n" +
                string.Join("\n", differences.Take(15)));
        }

        // Matching ignores case, so lower-casing must agree with JavaScript's toLowerCase() on every character
        // (JS's own table is tests/golden/lowercase.golden.json). The exceptions, none of which occurs in
        // Philippine job listings, skill names or place names:
        //   - U+0130 (capital I with a dot): JavaScript makes it "i" + a combining dot, .NET keeps it as it is;
        //   - capital letters added to Unicode 16 or later (Cyrillic U+1C89, Latin Extended-D U+A7CB-A7DC, Garay
        //     U+10D50-10D65, Beria Erfe U+16EA0-16EB8): this .NET runtime's tables predate them, so it leaves them as
        //     they are. Anything else that differs fails the test.
        // (A final capital sigma is context-dependent in JavaScript and is not tested: characters are compared one at a time.)
        [Fact]
        public void Lower_casing_agrees_with_JavaScript_on_every_character()
        {
            var golden = Load("lowercase.golden.json");

            var table = golden.GetProperty("map").EnumerateObject()
                .ToDictionary(entry => Convert.ToInt32(entry.Name, 16), entry => string.Concat(entry.Value.GetString()!.Split(' ').Select(hex => char.ConvertFromUtf32(Convert.ToInt32(hex, 16)))));

            static bool NewerThanTheRuntime(int codePoint) =>
                codePoint is 0x1C89 or (>= 0xA7CB and <= 0xA7DC) or (>= 0x10D50 and <= 0x10D65) or (>= 0x16EA0 and <= 0x16EB8);

            var differences = new List<string>();

            for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
            {
                if (codePoint is >= 0xD800 and <= 0xDFFF)
                    continue;

                var original = char.ConvertFromUtf32(codePoint);
                var expected = table.TryGetValue(codePoint, out var lower) ? lower : original;

                if (codePoint == 0x130 || (NewerThanTheRuntime(codePoint) && JsCompat.Lower(original) == original))
                    continue;

                if (JsCompat.Lower(original) != expected)
                    differences.Add($"U+{codePoint:X4}: JavaScript {string.Join(" ", expected.Select(c => ((int)c).ToString("X4")))}, C# {string.Join(" ", JsCompat.Lower(original).Select(c => ((int)c).ToString("X4")))}");
            }

            Assert.True(differences.Count == 0, $"{differences.Count} characters are lower-cased differently:\n" + string.Join("\n", differences.Take(100)));
            Assert.True(table.Count > 1000);
        }
    }
}
