using JobLinkv2.Services.Apply;
using Xunit;

namespace Joblink.Tests
{
    public class ApplyLinkSelectorTests
    {
        private const string Direct = "https://jobs.acme.example/apply/1";
        private const string Aggregator = "https://www.linkedin.com/jobs/view/1";

        private static string Options(params (string Link, bool IsDirect, string Publisher)[] entries) =>
            "[" + string.Join(",", entries.Select(e =>
                $"{{\"publisher\":\"{e.Publisher}\",\"apply_link\":\"{e.Link}\",\"is_direct\":{(e.IsDirect ? "true" : "false")}}}")) + "]";

        // ----- the four priorities, in order ------------------------------------

        [Fact]
        public void A_applyUrl_wins_when_it_is_direct()
        {
            var options = Options(("https://direct.example/opt", true, "Company site"));

            var link = ApplyLinkSelector.Choose(Direct, applyIsDirect: true, options, "Acme");

            Assert.Equal(Direct, link!.Url);
            Assert.Equal("Acme", link.Publisher);
        }

        [Fact]
        public void B_first_direct_option_beats_a_non_direct_applyUrl()
        {
            var options = Options(
                ("https://www.indeed.com/viewjob?jk=1", false, "Indeed"),
                ("https://careers.acme.example/job/1", true, "Acme Careers"));

            var link = ApplyLinkSelector.Choose(Aggregator, applyIsDirect: false, options, "LinkedIn");

            Assert.Equal("https://careers.acme.example/job/1", link!.Url);
            Assert.Equal("Acme Careers", link.Publisher);
        }

        [Fact]
        public void C_applyUrl_is_used_when_no_option_is_direct()
        {
            var options = Options(("https://www.indeed.com/viewjob?jk=1", false, "Indeed"));

            var link = ApplyLinkSelector.Choose(Aggregator, applyIsDirect: false, options, "LinkedIn");

            Assert.Equal(Aggregator, link!.Url);
            Assert.Equal("LinkedIn", link.Publisher);
        }

        [Fact]
        public void D_first_option_is_the_last_resort_when_there_is_no_applyUrl()
        {
            var options = Options(
                ("https://www.indeed.com/viewjob?jk=1", false, "Indeed"),
                ("https://www.glassdoor.com/job/1", false, "Glassdoor"));

            var link = ApplyLinkSelector.Choose(null, applyIsDirect: false, options, "LinkedIn");

            Assert.Equal("https://www.indeed.com/viewjob?jk=1", link!.Url);
            Assert.Equal("Indeed", link.Publisher);
        }

        [Fact]
        public void Priority_order_a_then_b_then_c_then_d_as_candidates_disappear()
        {
            var options = Options(
                ("https://opt-plain.example/1", false, "Plain"),
                ("https://opt-direct.example/2", true, "Direct"));

            Assert.Equal(Direct, ApplyLinkSelector.Choose(Direct, true, options)!.Url);                                   // a
            Assert.Equal("https://opt-direct.example/2", ApplyLinkSelector.Choose(Direct, false, options)!.Url);           // b
            Assert.Equal(Direct, ApplyLinkSelector.Choose(Direct, false, Options(("https://opt-plain.example/1", false, "P")))!.Url); // c
            Assert.Equal("https://opt-plain.example/1", ApplyLinkSelector.Choose(null, false, Options(("https://opt-plain.example/1", false, "P")))!.Url); // d
        }

        // ----- expired links are skipped ---------------------------------------

        [Theory]
        [InlineData("https://jobs.example/expired/123")]
        [InlineData("https://jobs.example/apply?status=EXPIRED")]
        [InlineData("https://Expired.example/apply")]
        public void Links_containing_expired_are_never_chosen(string deadLink)
        {
            Assert.Null(ApplyLinkSelector.Choose(deadLink, true, null));
            Assert.Null(ApplyLinkSelector.Choose(deadLink, false, null));
            Assert.Null(ApplyLinkSelector.Choose(null, false, Options((deadLink, true, "X"))));
        }

        [Fact]
        public void An_expired_top_choice_falls_through_to_the_next_candidate()
        {
            var options = Options(
                ("https://jobs.example/expired/direct", true, "Dead direct"),
                ("https://alive.example/direct", true, "Alive direct"));

            // (a) is expired -> (b) skips the expired direct option and takes the next direct one.
            var link = ApplyLinkSelector.Choose("https://jobs.example/expired/1", true, options);

            Assert.Equal("https://alive.example/direct", link!.Url);
        }

        [Fact]
        public void The_first_USABLE_option_is_taken_not_just_the_first_entry()
        {
            var options = Options(
                ("https://jobs.example/expired/1", false, "Dead"),
                ("http://insecure.example/2", false, "Insecure"),
                ("https://alive.example/3", false, "Alive"));

            var link = ApplyLinkSelector.Choose(null, false, options);

            Assert.Equal("https://alive.example/3", link!.Url);
            Assert.Equal("Alive", link.Publisher);
        }

        [Fact]
        public void Nothing_usable_returns_null()
        {
            var options = Options(("https://jobs.example/expired/1", true, "Dead"), ("http://x.example", false, "Insecure"));

            Assert.Null(ApplyLinkSelector.Choose("https://jobs.example/expired/2", true, options));
            Assert.Null(ApplyLinkSelector.Choose(null, false, null));
            Assert.Null(ApplyLinkSelector.Choose("", false, "[]"));
        }

        // ----- only safe URLs ----------------------------------------------------

        [Theory]
        [InlineData("http://jobs.example/apply")]              // not https
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html,<script>alert(1)</script>")]
        [InlineData("ftp://jobs.example/apply")]
        [InlineData("//jobs.example/apply")]                    // protocol-relative
        [InlineData("/apply/1")]                                // relative
        [InlineData("jobs.example/apply")]                      // no scheme
        [InlineData("https://user:pass@jobs.example/apply")]    // credentials in the URL
        [InlineData("https://linkedin.com@evil.example/apply")] // look-alike host trick
        [InlineData("   ")]
        public void Only_absolute_https_links_without_credentials_are_accepted(string candidate)
        {
            Assert.Null(ApplyLinkSelector.Normalize(candidate));
            Assert.Null(ApplyLinkSelector.Choose(candidate, true, null));
        }

        [Fact]
        public void Overlong_urls_are_rejected()
        {
            var huge = "https://jobs.example/" + new string('a', 2100);

            Assert.Null(ApplyLinkSelector.Normalize(huge));
        }

        [Fact]
        public void The_url_that_comes_back_is_the_normalised_one()
        {
            Assert.Equal("https://jobs.example/a%20b", ApplyLinkSelector.Normalize("https://JOBS.example/a b"));
        }

        // ----- bad option data does not break selection -------------------------

        [Theory]
        [InlineData("not json at all")]
        [InlineData("{\"apply_link\":\"https://x.example\"}")]   // object, not an array
        [InlineData("[1, \"two\", null]")]
        [InlineData("[{\"publisher\":\"NoLink\"}]")]
        [InlineData("[{\"apply_link\":42,\"is_direct\":true}]")]
        public void Malformed_options_are_ignored_and_the_applyUrl_still_works(string options)
        {
            Assert.Equal(Aggregator, ApplyLinkSelector.Choose(Aggregator, false, options)!.Url);
            Assert.Null(ApplyLinkSelector.Choose(null, false, options));
        }

        [Fact]
        public void An_option_that_is_direct_only_when_the_flag_is_literally_true()
        {
            var options = "[{\"apply_link\":\"https://a.example/1\",\"is_direct\":\"true\"},{\"apply_link\":\"https://b.example/2\",\"is_direct\":true}]";

            // "true" as a string is not direct, so (b) picks the second entry.
            Assert.Equal("https://b.example/2", ApplyLinkSelector.Choose(Aggregator, false, options)!.Url);
        }
    }
}
