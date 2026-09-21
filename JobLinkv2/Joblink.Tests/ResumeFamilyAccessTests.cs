using System.Net;
using System.Text.Json;
using Joblink.Services.Resumes;
using Xunit;

namespace Joblink.Tests
{
    // Profile, resumes, education, experience, resume skills and job preferences
    // used to be readable and writable by anyone, for any user. These are the
    // checks that need no database: who may call what, and what is refused before
    // a request gets anywhere near the data. (The store is pointed at a server that
    // isn't there, so a test that wrongly reaches it fails.) What each user can
    // reach in the data is in ResumeFamilyDbTests.
    public class ResumeFamilyAccessTests : EndpointTestBase
    {
        public ResumeFamilyAccessTests(Support.ApiFactory factory) : base(factory) { }

        private const string Ids = "{\"resumeId\":1,\"educationId\":1,\"experienceId\":1,\"skillId\":1}";

        public static IEnumerable<object[]> JobSeekerEndpoints() => new[]
        {
            new object?[] { "GET", "/api/Resume/1", null },
            new object?[] { "GET", "/api/Resume/by-user/1", null },
            new object?[] { "POST", "/api/Resume", "{}" },
            new object?[] { "PUT", "/api/Resume", Ids },
            new object?[] { "DELETE", "/api/Resume?id=1", null },

            new object?[] { "GET", "/api/Education/1", null },
            new object?[] { "GET", "/api/Education/by-resume/1", null },
            new object?[] { "POST", "/api/Education", Ids },
            new object?[] { "PUT", "/api/Education", Ids },
            new object?[] { "DELETE", "/api/Education?id=1", null },

            new object?[] { "GET", "/api/Experience/1", null },
            new object?[] { "GET", "/api/Experience/by-resume/1", null },
            new object?[] { "POST", "/api/Experience", Ids },
            new object?[] { "PUT", "/api/Experience", Ids },
            new object?[] { "DELETE", "/api/Experience?id=1", null },

            new object?[] { "GET", "/api/ResumeSkills/by-resume/1", null },
            new object?[] { "POST", "/api/ResumeSkills", Ids },
            new object?[] { "DELETE", "/api/ResumeSkills/1/1", null },

            new object?[] { "GET", "/api/JobPreference/by-user/1", null },
            new object?[] { "PUT", "/api/JobPreference/by-user/1", "{}" }
        };

        public static IEnumerable<object[]> ProfileEndpoints() => new[]
        {
            new object?[] { "GET", "/api/Profile/1", null },
            new object?[] { "GET", "/api/Profile/by-user/1", null },
            new object?[] { "POST", "/api/Profile", "{}" },
            new object?[] { "PUT", "/api/Profile", "{}" },
            new object?[] { "DELETE", "/api/Profile?id=1", null }
        };

        private static string? BodyFor(string method, string? body) => method is "POST" or "PUT" ? body ?? "{}" : null;

        // ----- who may call ------------------------------------------------------

        [Theory]
        [MemberData(nameof(JobSeekerEndpoints))]
        [MemberData(nameof(ProfileEndpoints))]
        public async Task Every_endpoint_needs_a_login(string method, string url, string? body)
        {
            var response = await Send(Factory.CreateClient(), new HttpMethod(method), url, BodyFor(method, body));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(JobSeekerEndpoints))]
        public async Task Employers_have_no_access_to_job_seeker_data(string method, string url, string? body)
        {
            var response = await Send(Client(NewUser(), "employer"), new HttpMethod(method), url, BodyFor(method, body));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ----- what no longer exists -------------------------------------------

        [Theory]
        [InlineData("GET", "/api/Profile")]
        [InlineData("GET", "/api/Resume")]
        [InlineData("GET", "/api/Education")]
        [InlineData("GET", "/api/Experience")]
        [InlineData("GET", "/api/ResumeSkills")]
        [InlineData("GET", "/api/ResumeSkills/1")]
        [InlineData("PUT", "/api/ResumeSkills")]
        [InlineData("DELETE", "/api/ResumeSkills?id=1")]
        public async Task The_list_everything_and_by_id_routes_are_gone(string method, string url)
        {
            var anonymous = await Send(Factory.CreateClient(), new HttpMethod(method), url, method == "PUT" ? "{}" : null);
            var loggedIn = await Send(Client(NewUser()), new HttpMethod(method), url, method == "PUT" ? "{}" : null);

            Assert.Contains(anonymous.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
            Assert.Contains(loggedIn.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }

        // ----- somebody else's id in the URL -----------------------------------

        [Theory]
        [InlineData("GET", "/api/Profile/by-user/{0}")]
        [InlineData("GET", "/api/Resume/by-user/{0}")]
        [InlineData("GET", "/api/JobPreference/by-user/{0}")]
        [InlineData("PUT", "/api/JobPreference/by-user/{0}")]
        public async Task Asking_for_another_users_data_by_their_id_is_a_403(string method, string pattern)
        {
            var me = NewUser();
            var someoneElse = NewUser();

            var response = await Send(Client(me), new HttpMethod(method), string.Format(pattern, someoneElse), method == "PUT" ? "{}" : null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ----- refused before it reaches the data ------------------------------

        public static IEnumerable<object[]> BadInput() => new[]
        {
            new object[] { "POST", "/api/Profile", "{\"phone\":\"123456789012345678901\"}", "Phone" },
            new object[] { "POST", "/api/Profile", "{\"linkedinUrl\":\"javascript:alert(1)\"}", "http" },
            new object[] { "POST", "/api/Profile", "{\"githubUrl\":\"ftp://example.com/a\"}", "http" },
            new object[] { "POST", "/api/Profile", "{\"linkedinUrl\":\"https://user:pass@example.com\"}", "http" },
            new object[] { "PUT", "/api/Resume", "{}", "ResumeId" },
            new object[] { "PUT", "/api/Resume", "{\"resumeId\":1,\"title\":\"" + new string('t', 101) + "\"}", "Title" },
            new object[] { "POST", "/api/Resume", "{\"title\":\"" + new string('t', 101) + "\"}", "Title" },
            new object[] { "POST", "/api/Education", "{}", "resume id" },
            new object[] { "PUT", "/api/Education", "{\"resumeId\":1}", "education id" },
            new object[] { "POST", "/api/Education", "{\"resumeId\":1,\"startDate\":\"not a date\"}", "isn't a date" },
            new object[] { "POST", "/api/Education", "{\"resumeId\":1,\"startDate\":12345}", "date" },
            new object[] { "POST", "/api/Education", "{\"resumeId\":1,\"schoolName\":\"" + new string('s', 101) + "\"}", "SchoolName" },
            new object[] { "POST", "/api/Experience", "{}", "resume id" },
            new object[] { "PUT", "/api/Experience", "{\"resumeId\":1}", "experience id" },
            new object[] { "PUT", "/api/Experience", "{\"experienceId\":1,\"endDate\":\"2024-13\"}", "isn't a date" },
            new object[] { "POST", "/api/Experience", "{\"resumeId\":1,\"description\":\"" + new string('d', 20001) + "\"}", "Description" },
            new object[] { "POST", "/api/ResumeSkills", "{\"resumeId\":1}", "SkillId" },
            new object[] { "POST", "/api/ResumeSkills", "{\"skillId\":1}", "ResumeId" },
            new object[] { "POST", "/api/ResumeSkills", "{}", "required" }
        };

        [Theory]
        [MemberData(nameof(BadInput))]
        public async Task Bad_input_is_a_400_with_a_message_and_never_reaches_the_data(string method, string url, string body, string mention)
        {
            var response = await Send(Client(NewUser()), new HttpMethod(method), url, body);
            var text = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(text).RootElement;

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid", json.GetProperty("code").GetString());
            Assert.Contains(mention, json.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("{\"workArrangement\":\"mars\"}", "onsite, remote or hybrid")]
        [InlineData("{\"minSalary\":-5}", "negative")]
        [InlineData("{\"minSalary\":50000,\"maxSalary\":40000}", "higher")]
        [InlineData("{\"maxSalary\":100000001}", "too large")]
        public async Task Job_preferences_keep_their_checks(string body, string mention)
        {
            var me = NewUser();

            var response = await Send(Client(me), HttpMethod.Put, $"/api/JobPreference/by-user/{me}", body);
            var json = await Read(response);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(mention, json.GetProperty("message").GetString());
        }

        // ----- dates ---------------------------------------------------------------

        private static SaveEducationRequest? Parse(string json) =>
            JsonSerializer.Deserialize<SaveEducationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        [Theory]
        [InlineData("\"2021-05\"", 2021, 5, 1)]                    // the Resume Builder's month picker
        [InlineData("\"2021-05-31\"", 2021, 5, 31)]
        [InlineData("\"2021-05-31T13:45:00\"", 2021, 5, 31)]       // what the API sends back
        [InlineData("\"2021-05-31T13:45:00.1234567\"", 2021, 5, 31)]
        [InlineData("\"2021-05-31T13:45:00Z\"", 2021, 5, 31)]
        [InlineData("\" 2021-05 \"", 2021, 5, 1)]
        public void Month_and_date_formats_are_understood(string json, int year, int month, int day)
        {
            var parsed = Parse($"{{\"startDate\":{json}}}")!;

            Assert.Equal(new DateTime(year, month, day), parsed.StartDate!.Value.Date);
            Assert.Equal(TimeSpan.Zero, parsed.StartDate.Value.TimeOfDay);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("\"\"")]
        [InlineData("\"   \"")]
        public void An_empty_date_is_no_date(string json)
        {
            Assert.Null(Parse($"{{\"endDate\":{json}}}")!.EndDate);
        }

        [Theory]
        [InlineData("\"May 2021\"")]
        [InlineData("\"2021-13\"")]
        [InlineData("\"2021-02-30\"")]
        [InlineData("\"tomorrow\"")]
        [InlineData("20210501")]
        [InlineData("true")]
        public void Anything_else_is_refused(string json)
        {
            Assert.Throws<JsonException>(() => Parse($"{{\"startDate\":{json}}}"));
        }

        [Fact]
        public void Blank_boxes_become_no_value_and_text_is_trimmed()
        {
            var fields = Parse("{\"schoolName\":\"  UP Diliman  \",\"degree\":\"   \"}")!.ToFields();

            Assert.Equal("UP Diliman", fields.SchoolName);
            Assert.Null(fields.Degree);
        }
    }
}
