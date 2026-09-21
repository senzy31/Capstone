using System.Text.Json;
using JobLinkv2.Services.Apply;
using Xunit;

namespace Joblink.Tests
{
    public class JSearchListingMapperTests
    {
        // A trimmed copy of a real search-v2 job.
        private const string RealJob = @"{
            ""job_id"": ""R0FiODhnUTk0bUkzSElu"",
            ""job_uid"": ""GAb88gQ94mI3HInWAAAAAA=="",
            ""job_title"": ""Fullstack Developer"",
            ""employer_name"": ""Ascendion"",
            ""job_publisher"": ""BeBee"",
            ""job_apply_link"": ""https://bebee.com/ph/jobs/fullstack-developer"",
            ""job_apply_is_direct"": false,
            ""apply_options"": [
                { ""publisher"": ""BeBee"", ""apply_link"": ""https://bebee.com/ph/jobs/fullstack-developer"", ""is_direct"": false },
                { ""publisher"": ""Ascendion Careers"", ""apply_link"": ""https://ascendion.example/jobs/9"", ""is_direct"": true }
            ],
            ""job_description"": ""We use React & Node.js."",
            ""job_posted_at_datetime_utc"": ""2026-09-17T00:00:00.000Z"",
            ""job_location"": ""Tawi-Tawi, Philippines"",
            ""job_city"": null,
            ""job_state"": ""Tawi-Tawi"",
            ""job_country"": ""PH"",
            ""job_latitude"": 5.133811,
            ""job_longitude"": 119.950926
        }";

        private static ImportedListing Map(string json)
        {
            using var document = JsonDocument.Parse(json);

            Assert.True(JSearchListingMapper.TryMap(document.RootElement, out var listing));

            return listing!;
        }

        [Fact]
        public void Maps_the_fields_the_apply_flow_needs()
        {
            var listing = Map(RealJob);

            Assert.Equal("R0FiODhnUTk0bUkzSElu", listing.ExternalJobId);                       // job_id -> external_job_id
            Assert.Equal("https://bebee.com/ph/jobs/fullstack-developer", listing.ApplyUrl);   // job_apply_link -> apply_url
            Assert.False(listing.ApplyIsDirect);                                              // job_apply_is_direct -> apply_is_direct
            Assert.Equal("BeBee", listing.Publisher);                                         // job_publisher -> publisher
            Assert.Equal(5.133811m, listing.Latitude);                                         // job_latitude / job_longitude
            Assert.Equal(119.950926m, listing.Longitude);
        }

        [Fact]
        public void Keeps_apply_options_as_the_original_JSON_array()
        {
            var listing = Map(RealJob);

            using var options = JsonDocument.Parse(listing.ApplyOptionsJson!);

            Assert.Equal(JsonValueKind.Array, options.RootElement.ValueKind);
            Assert.Equal(2, options.RootElement.GetArrayLength());
            Assert.Equal("Ascendion Careers", options.RootElement[1].GetProperty("publisher").GetString());
            Assert.True(options.RootElement[1].GetProperty("is_direct").GetBoolean());
        }

        [Fact]
        public void Maps_the_descriptive_fields_too()
        {
            var listing = Map(RealJob);

            Assert.Equal("Fullstack Developer", listing.Title);
            Assert.Equal("Ascendion", listing.Company);
            Assert.Equal("Tawi-Tawi, Philippines", listing.Location);
            Assert.Equal("We use React & Node.js.", listing.Description);
            Assert.Equal(new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc), listing.PostedDate);
        }

        [Fact]
        public void The_mapped_apply_data_feeds_the_link_selector_end_to_end()
        {
            var listing = Map(RealJob);

            var link = ApplyLinkSelector.Choose(listing.ApplyUrl, listing.ApplyIsDirect, listing.ApplyOptionsJson, listing.Publisher);

            // apply_url isn't direct, so the direct option (the company's own site) wins.
            Assert.Equal("https://ascendion.example/jobs/9", link!.Url);
            Assert.Equal("Ascendion Careers", link.Publisher);
        }

        [Fact]
        public void The_direct_flag_is_carried_over()
        {
            var listing = Map(RealJob.Replace("\"job_apply_is_direct\": false", "\"job_apply_is_direct\": true"));

            Assert.True(listing.ApplyIsDirect);
        }

        [Fact]
        public void Builds_the_location_from_its_parts_when_job_location_is_missing()
        {
            var listing = Map(@"{ ""job_id"": ""x"", ""job_city"": ""Makati"", ""job_state"": ""Metro Manila"", ""job_country"": ""PH"" }");

            Assert.Equal("Makati, Metro Manila, PH", listing.Location);
        }

        [Fact]
        public void Falls_back_to_the_short_uid_when_the_job_id_is_too_long()
        {
            var listing = Map($@"{{ ""job_id"": ""{new string('a', 901)}"", ""job_uid"": ""SHORT123=="" }}");

            Assert.Equal("uid:SHORT123==", listing.ExternalJobId);
        }

        [Theory]
        [InlineData(@"{ ""job_title"": ""no id at all"" }")]
        [InlineData(@"{ ""job_id"": """", ""job_uid"": """" }")]
        [InlineData(@"[]")]
        [InlineData(@"""just a string""")]
        public void Jobs_that_cannot_be_identified_are_skipped(string json)
        {
            using var document = JsonDocument.Parse(json);

            Assert.False(JSearchListingMapper.TryMap(document.RootElement, out var listing));
            Assert.Null(listing);
        }

        [Fact]
        public void Missing_apply_fields_become_null_or_false_not_errors()
        {
            var listing = Map(@"{ ""job_id"": ""abc"" }");

            Assert.Null(listing.ApplyUrl);
            Assert.False(listing.ApplyIsDirect);
            Assert.Null(listing.Publisher);
            Assert.Null(listing.ApplyOptionsJson);
            Assert.Null(listing.Latitude);
            Assert.Null(listing.Longitude);
        }

        [Fact]
        public void Coordinates_out_of_range_or_not_numbers_are_dropped_and_others_rounded()
        {
            var bad = Map(@"{ ""job_id"": ""a"", ""job_latitude"": 123.4, ""job_longitude"": ""east"" }");
            var rounded = Map(@"{ ""job_id"": ""b"", ""job_latitude"": 14.55987654321, ""job_longitude"": -121.0000004 }");

            Assert.Null(bad.Latitude);
            Assert.Null(bad.Longitude);
            Assert.Equal(14.559877m, rounded.Latitude);
            Assert.Equal(-121.0m, rounded.Longitude);
        }

        [Fact]
        public void Apply_options_that_are_not_an_array_are_not_stored()
        {
            var listing = Map(@"{ ""job_id"": ""a"", ""apply_options"": { ""apply_link"": ""https://x.example"" } }");

            Assert.Null(listing.ApplyOptionsJson);
        }

        [Fact]
        public void Long_text_is_trimmed_to_the_column_sizes()
        {
            var listing = Map($@"{{ ""job_id"": ""a"", ""job_title"": ""{new string('t', 500)}"", ""job_publisher"": ""{new string('p', 500)}"" }}");

            Assert.Equal(300, listing.Title!.Length);
            Assert.Equal(200, listing.Publisher!.Length);
        }
    }
}
