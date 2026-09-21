using System.Globalization;
using System.Text.Json;

namespace JobLinkv2.Services.Apply
{
    // Maps one job from JSearch's search-v2 response onto a Job_Listings row:
    //   job_id -> external_job_id          job_apply_link -> apply_url
    //   job_apply_is_direct -> apply_is_direct   job_publisher -> publisher
    //   apply_options -> apply_options (JSON)    job_latitude/longitude -> coordinates
    // Imported jobs are always source 'External' with no employer.
    public static class JSearchListingMapper
    {
        // Column sizes in Job_Listings.
        private const int MaxExternalIdLength = 900;
        private const int MaxTextLength = 300;
        private const int MaxPublisherLength = 200;
        private const int MaxUrlLength = 2000;
        private const int MaxOptionsJsonLength = 100_000;

        public static bool TryMap(JsonElement job, out ImportedListing? listing)
        {
            listing = null;

            if (job.ValueKind != JsonValueKind.Object)
                return false;

            var externalId = ReadString(job, "job_id");

            // JSearch ids are ~400 chars; fall back to the short uid if the id is
            // missing or ever too long. A job with neither can't be told apart from
            // other jobs, so it isn't imported.
            if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > MaxExternalIdLength)
            {
                var uid = ReadString(job, "job_uid");

                if (string.IsNullOrWhiteSpace(uid))
                    return false;

                externalId = "uid:" + uid;
            }

            listing = new ImportedListing(
                ExternalJobId: externalId,
                Title: Truncate(ReadString(job, "job_title"), MaxTextLength),
                Company: Truncate(ReadString(job, "employer_name"), MaxTextLength),
                Location: Truncate(BuildLocation(job), MaxTextLength),
                Description: ReadString(job, "job_description"),
                PostedDate: ReadDate(job, "job_posted_at_datetime_utc"),
                ApplyUrl: Truncate(ReadString(job, "job_apply_link"), MaxUrlLength),
                ApplyIsDirect: job.TryGetProperty("job_apply_is_direct", out var direct) && direct.ValueKind == JsonValueKind.True,
                Publisher: Truncate(ReadString(job, "job_publisher"), MaxPublisherLength),
                ApplyOptionsJson: ReadOptionsJson(job),
                Latitude: ReadCoordinate(job, "job_latitude", 90),
                Longitude: ReadCoordinate(job, "job_longitude", 180));

            return true;
        }

        private static string? BuildLocation(JsonElement job)
        {
            var location = ReadString(job, "job_location");

            if (!string.IsNullOrWhiteSpace(location))
                return location;

            var parts = new[] { ReadString(job, "job_city"), ReadString(job, "job_state"), ReadString(job, "job_country") }
                .Where(part => !string.IsNullOrWhiteSpace(part));

            var joined = string.Join(", ", parts);

            return joined.Length > 0 ? joined : null;
        }

        private static string? ReadOptionsJson(JsonElement job)
        {
            if (!job.TryGetProperty("apply_options", out var options) || options.ValueKind != JsonValueKind.Array)
                return null;

            var json = options.GetRawText();

            return json.Length <= MaxOptionsJsonLength ? json : null;
        }

        private static decimal? ReadCoordinate(JsonElement job, string property, double limit)
        {
            if (!job.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
                return null;

            var number = value.GetDouble();

            if (double.IsNaN(number) || Math.Abs(number) > limit)
                return null;

            return Math.Round((decimal)number, 6);
        }

        private static DateTime? ReadDate(JsonElement job, string property)
        {
            var text = ReadString(job, property);

            return text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
                ? date
                : null;
        }

        private static string? ReadString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? Truncate(string? text, int max) =>
            text is null || text.Length <= max ? text : text[..max];
    }
}
