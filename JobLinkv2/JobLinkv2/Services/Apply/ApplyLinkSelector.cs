using System.Text.Json;

namespace JobLinkv2.Services.Apply
{
    public sealed record ApplyLink(string Url, string? Publisher);

    // Picks where to send a user who clicks Apply on an external job.
    //
    // Only links stored on the job listing are ever considered - never anything
    // the client sends - and a link must be a plain https URL that isn't marked
    // "expired". The first usable candidate in this order wins:
    //   a) apply_url, when apply_is_direct is true
    //   b) the first apply_options entry with is_direct = true
    //   c) apply_url
    //   d) the first apply_options entry
    public static class ApplyLinkSelector
    {
        private const int MaxUrlLength = 2000;

        public static ApplyLink? Choose(string? applyUrl, bool applyIsDirect, string? applyOptionsJson, string? listingPublisher = null)
        {
            var options = ParseOptions(applyOptionsJson);

            // (a)
            if (applyIsDirect && Normalize(applyUrl) is { } directUrl)
                return new ApplyLink(directUrl, listingPublisher);

            // (b)
            foreach (var option in options)
                if (option.IsDirect && Normalize(option.Url) is { } url)
                    return new ApplyLink(url, option.Publisher ?? listingPublisher);

            // (c)
            if (Normalize(applyUrl) is { } mainUrl)
                return new ApplyLink(mainUrl, listingPublisher);

            // (d)
            foreach (var option in options)
                if (Normalize(option.Url) is { } url)
                    return new ApplyLink(url, option.Publisher ?? listingPublisher);

            return null;
        }

        // The normalised absolute https URL, or null if it can't be trusted.
        public static string? Normalize(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxUrlLength)
                return null;

            if (candidate.Contains("expired", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
                return null;

            // https only. Rejecting user info also blocks look-alike links such
            // as https://linkedin.com@evil.example/.
            if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || string.IsNullOrEmpty(uri.Host))
                return null;

            var normalized = uri.AbsoluteUri;

            return normalized.Contains("expired", StringComparison.OrdinalIgnoreCase) ? null : normalized;
        }

        private sealed record Option(string? Url, bool IsDirect, string? Publisher);

        private static List<Option> ParseOptions(string? json)
        {
            var options = new List<Option>();

            if (string.IsNullOrWhiteSpace(json))
                return options;

            try
            {
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return options;

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    options.Add(new Option(
                        ReadString(item, "apply_link"),
                        item.TryGetProperty("is_direct", out var direct) && direct.ValueKind == JsonValueKind.True,
                        ReadString(item, "publisher")));
                }
            }
            catch (JsonException)
            {
                // Corrupt JSON just means there are no options to offer.
            }

            return options;
        }

        private static string? ReadString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
