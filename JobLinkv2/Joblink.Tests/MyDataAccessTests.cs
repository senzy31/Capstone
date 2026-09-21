using System.Net;
using System.Text.Json;
using Xunit;

namespace Joblink.Tests
{
    // Notifications, saved jobs, job matches and skills used to be readable and writable
    // by anyone, for any user. These are the checks that need no database: who may call
    // what, which routes are gone, and what is refused before it reaches the data. (The
    // stores point at a server that isn't there, so a test that wrongly reaches one fails.)
    // What each user can reach in the data is in MyDataDbTests.
    public class MyDataAccessTests : EndpointTestBase
    {
        public MyDataAccessTests(Support.ApiFactory factory) : base(factory) { }

        private static string? BodyFor(string method, string? body) => method is "POST" or "PUT" ? body ?? "{}" : null;

        public static IEnumerable<object[]> NeedsALogin() => new[]
        {
            new object?[] { "GET", "/api/Notification", null },
            new object?[] { "GET", "/api/Notification/1", null },
            new object?[] { "PUT", "/api/Notification", "{\"notificationId\":1}" },
            new object?[] { "DELETE", "/api/Notification?id=1", null },

            new object?[] { "GET", "/api/SavedJobs", null },
            new object?[] { "POST", "/api/SavedJobs", "{\"jobId\":1}" },
            new object?[] { "DELETE", "/api/SavedJobs/1", null },

            new object?[] { "GET", "/api/JobMatch", null },
            new object?[] { "GET", "/api/JobMatch/1", null },

            new object?[] { "POST", "/api/Skills", "{\"skillName\":\"SQL\"}" }
        };

        // Job-seeker features: an employer is refused (Notifications are for everyone).
        public static IEnumerable<object[]> JobSeekerOnly() => new[]
        {
            new object?[] { "GET", "/api/SavedJobs", null },
            new object?[] { "POST", "/api/SavedJobs", "{\"jobId\":1}" },
            new object?[] { "DELETE", "/api/SavedJobs/1", null },
            new object?[] { "GET", "/api/JobMatch", null },
            new object?[] { "GET", "/api/JobMatch/1", null },
            new object?[] { "POST", "/api/Skills", "{\"skillName\":\"SQL\"}" }
        };

        [Theory]
        [MemberData(nameof(NeedsALogin))]
        public async Task Every_endpoint_that_isnt_public_needs_a_login(string method, string url, string? body)
        {
            var response = await Send(Factory.CreateClient(), new HttpMethod(method), url, BodyFor(method, body));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(JobSeekerOnly))]
        public async Task Employers_are_refused_on_job_seeker_features(string method, string url, string? body)
        {
            var response = await Send(Client(NewUser(), "employer"), new HttpMethod(method), url, BodyFor(method, body));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ----- what no longer exists -------------------------------------------

        [Theory]
        [InlineData("POST", "/api/Notification")]           // notifications are made by the server
        [InlineData("POST", "/api/JobMatch")]               // so are matches: a client could give itself a perfect score
        [InlineData("PUT", "/api/JobMatch")]
        [InlineData("DELETE", "/api/JobMatch?id=1")]
        [InlineData("PUT", "/api/SavedJobs")]
        [InlineData("DELETE", "/api/SavedJobs?id=1")]
        [InlineData("GET", "/api/SavedJobs/1")]
        [InlineData("PUT", "/api/Skills")]                  // a rename would change every resume that uses it
        [InlineData("DELETE", "/api/Skills?id=1")]
        [InlineData("DELETE", "/api/Skills/1")]
        public async Task These_routes_are_gone(string method, string url)
        {
            var anonymous = await Send(Factory.CreateClient(), new HttpMethod(method), url, BodyFor(method, "{}"));
            var loggedIn = await Send(Client(NewUser()), new HttpMethod(method), url, BodyFor(method, "{}"));

            Assert.Contains(anonymous.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
            Assert.Contains(loggedIn.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }

        // ----- refused before it reaches the data ------------------------------

        [Theory]
        [InlineData("POST", "/api/Skills", "{}", "SkillName")]
        [InlineData("POST", "/api/Skills", "{\"skillName\":\"\"}", "SkillName")]
        [InlineData("POST", "/api/Skills", "{\"skillName\":\"   \"}", "required")]
        [InlineData("POST", "/api/Skills", "{\"skillName\":\"日本語\"}", "letters, numbers")]
        [InlineData("POST", "/api/Skills", "{\"skillName\":\"bad\\u0007name\"}", "letters, numbers")]
        [InlineData("POST", "/api/SavedJobs", "{}", "JobId")]
        [InlineData("PUT", "/api/Notification", "{}", "NotificationId")]
        public async Task Bad_input_is_a_400_with_a_message_and_never_reaches_the_data(string method, string url, string body, string mention)
        {
            var response = await Send(Client(NewUser()), new HttpMethod(method), url, body);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid", json.GetProperty("code").GetString());
            Assert.Contains(mention, json.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task A_skill_name_over_100_characters_is_refused()
        {
            var response = await Send(Client(NewUser()), HttpMethod.Post, "/api/Skills", $"{{\"skillName\":\"{new string('s', 101)}\"}}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
