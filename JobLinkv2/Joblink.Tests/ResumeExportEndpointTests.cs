using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Joblink.Tests.Support;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // GET /api/Resume/{id}/export through real routing, real JWT checks, the real database and the
    // real plan rules - with JobLink-AI (Python) faked (Support.FakeResumeDocumentService), the same
    // way ResumeFamilyDbTests uses the real database for everything else a resume touches. The point
    // of most of these: ownership, the Premium-only ATS template, and that what reaches the fake
    // service is built from the database, never from anything the caller sends beyond format/template.
    public sealed class ResumeExportEndpointTests : IClassFixture<DbApiFactory>, IDisposable
    {
        private readonly DbApiFactory _factory;
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        public ResumeExportEndpointTests(DbApiFactory factory) => _factory = factory;

        // ----- helpers ------------------------------------------------------------------------------

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser(string role = "user")
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', @role, GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com", role });

            _userIds.Add(id);

            return id;
        }

        private HttpClient As(int userId, string role = "user") => _factory.ClientFor(userId, role);

        private static Task<HttpResponseMessage> Call(HttpClient client, string method, string url, string? json = null) =>
            client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
            });

        private static async Task<JsonElement> Json(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private async Task<int> NewResume(int userId)
        {
            var response = await Call(As(userId), "POST", "/api/Resume", "{}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (await Json(response)).GetProperty("resumeId").GetInt32();
        }

        private static Task<HttpResponseMessage> Export(HttpClient client, int resumeId, string format = "pdf", string template = "harvard") =>
            client.GetAsync($"/api/Resume/{resumeId}/export?format={format}&template={template}");

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Experience    WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Resumes WHERE user_id IN @users;
                DELETE FROM Profiles WHERE user_id IN @users;
                DELETE FROM Subscriptions WHERE user_id IN @users;
                DELETE FROM Skills WHERE skill_name LIKE @skills;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, skills = $"dbtest-{_tag}-%" });
        }

        // ----- who may ask ----------------------------------------------------------------------------

        [DbFact]
        public async Task Export_needs_a_login()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/Resume/1/export?format=pdf&template=harvard")).StatusCode);
        }

        [DbFact]
        public async Task An_employer_cannot_use_the_job_seeker_resume_endpoint()
        {
            var response = await Export(As(NewUser("employer"), "employer"), 1);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [DbFact]
        public async Task Someone_elses_resume_is_a_404_and_nothing_was_asked_of_the_document_service()
        {
            var resumeId = await NewResume(NewUser());
            var before = _factory.Documents.PdfRequests.Count;

            var response = await Export(As(NewUser()), resumeId);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(before, _factory.Documents.PdfRequests.Count);
        }

        [DbFact]
        public async Task An_unknown_resume_id_is_also_a_404()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Export(As(NewUser()), 999999)).StatusCode);
        }

        // ----- format / template validation --------------------------------------------------------

        [DbFact]
        public async Task An_unknown_format_is_refused()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            var response = await Export(As(userId), resumeId, format: "exe");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("pdf", (await Json(response)).GetProperty("message").GetString());
        }

        // An empty query value (?format=) binds to null, the same as leaving format out completely
        // - ASP.NET Core's model binding, not something this endpoint decides - so it defaults to
        // pdf rather than being refused. (template has no such default: an empty or missing one
        // always fails - see An_unknown_template_is_refused - because there's no template it would
        // make sense to guess.)
        [DbFact]
        public async Task An_empty_format_defaults_to_pdf_the_same_as_a_missing_one()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);
            var client = As(userId);

            var empty = await Export(client, resumeId, format: "");
            var missing = await client.GetAsync($"/api/Resume/{resumeId}/export?template=harvard");

            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            Assert.Equal("application/pdf", empty.Content.Headers.ContentType?.MediaType);
            Assert.Equal("application/pdf", missing.Content.Headers.ContentType?.MediaType);
        }

        [DbFact]
        public async Task Format_is_read_case_insensitively()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            Assert.Equal(HttpStatusCode.OK, (await Export(As(userId), resumeId, format: "PDF")).StatusCode);
        }

        [DbFact]
        public async Task An_unknown_template_is_refused()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);
            var client = As(userId);

            foreach (var template in new[] { "fancy", "", "Standard" })
            {
                var response = await Export(client, resumeId, template: template);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("harvard", (await Json(response)).GetProperty("message").GetString());
            }
        }

        [DbFact]
        public async Task Every_free_template_works_for_a_free_account()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);
            var client = As(userId);

            foreach (var template in new[] { "harvard", "reverse_chronological", "functional" })
                Assert.Equal(HttpStatusCode.OK, (await Export(client, resumeId, template: template)).StatusCode);
        }

        // ----- the Premium gate --------------------------------------------------------------------

        [DbFact]
        public async Task The_ATS_template_is_refused_for_Free_with_the_same_upgrade_shape_as_other_limits()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);
            var before = _factory.Documents.PdfRequests.Count;

            var response = await Export(As(userId), resumeId, template: "ats");
            var body = await Json(response);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("upgrade_required", body.GetProperty("code").GetString());
            Assert.True(body.GetProperty("upgradeRequired").GetBoolean());
            Assert.Equal("advancedTemplates", body.GetProperty("feature").GetString());
            Assert.Contains("Premium", body.GetProperty("message").GetString());
            Assert.Equal(before, _factory.Documents.PdfRequests.Count);   // the document service was never asked
        }

        [DbFact]
        public async Task The_ATS_template_works_for_Premium()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);
            _factory.MakePremium(userId);

            Assert.Equal(HttpStatusCode.OK, (await Export(As(userId), resumeId, template: "ats")).StatusCode);
        }

        [DbFact]
        public async Task Upgrading_unlocks_ATS_on_the_same_token_and_lapsing_locks_it_again()
        {
            var userId = NewUser();
            var client = As(userId);
            var resumeId = await NewResume(userId);

            Assert.Equal(HttpStatusCode.Forbidden, (await Export(client, resumeId, template: "ats")).StatusCode);

            _factory.MakePremium(userId);
            Assert.Equal(HttpStatusCode.OK, (await Export(client, resumeId, template: "ats")).StatusCode);

            _factory.Clock.Advance(TimeSpan.FromDays(40));   // premium_until has passed - nothing ran, the same token
            Assert.Equal(HttpStatusCode.Forbidden, (await Export(client, resumeId, template: "ats")).StatusCode);
        }

        // ----- what the browser gets -----------------------------------------------------------------

        [DbFact]
        public async Task A_successful_export_returns_the_document_bytes_content_type_and_filename()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            var response = await Export(As(userId), resumeId, format: "pdf");
            var bytes = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("resume.pdf", response.Content.Headers.ContentDisposition?.FileName);
            Assert.Equal(0x25, bytes[0]);   // '%' of %PDF, from the fake
        }

        [DbFact]
        public async Task Docx_asks_the_document_service_for_docx_not_pdf()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            var before = (_factory.Documents.PdfRequests.Count, _factory.Documents.DocxRequests.Count);
            var response = await Export(As(userId), resumeId, format: "docx");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal((before.Item1, before.Item2 + 1), (_factory.Documents.PdfRequests.Count, _factory.Documents.DocxRequests.Count));
        }

        [DbFact]
        public async Task A_failure_from_the_document_service_is_passed_through_with_its_status_and_message()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            _factory.Documents.Fail = Joblink.Services.JobSearch.UpstreamResult<Joblink.Services.Resume.GeneratedDocument>.Failure(
                503, "The resume document service is not available right now.");

            try
            {
                var response = await Export(As(userId), resumeId);
                var body = await Json(response);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("The resume document service is not available right now.", body.GetProperty("message").GetString());
            }
            finally
            {
                _factory.Documents.Fail = null;
            }
        }

        // ----- what is sent is built from the database, never from the caller ---------------------------

        [DbFact]
        public async Task The_request_carries_the_callers_own_name_email_and_skills_never_anything_they_sent()
        {
            var userId = NewUser();
            var client = As(userId);
            var resumeId = await NewResume(userId);

            var skill = await Json(await Call(client, "POST", "/api/Skills", $"{{\"skillName\":\"dbtest-{_tag}-React\"}}"));
            await Call(client, "POST", "/api/ResumeSkills", $"{{\"resumeId\":{resumeId},\"skillId\":{skill.GetProperty("skillId").GetInt32()}}}");

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Resume/{resumeId}/export?format=pdf&template=harvard")
            {
                Content = new StringContent(
                    "{\"personalInfo\":{\"fullName\":\"Forged Name\",\"email\":\"forged@example.com\"},\"skills\":{\"technicalSkills\":[\"Hacked\"]}}",
                    Encoding.UTF8, "application/json")
            };

            await client.SendAsync(request);

            var sent = _factory.Documents.PdfRequests[^1];

            Assert.NotEqual("Forged Name", sent.PersonalInfo.FullName);
            Assert.NotEqual("forged@example.com", sent.PersonalInfo.Email);
            Assert.Equal(new[] { $"dbtest-{_tag}-React" }, sent.Skills.TechnicalSkills);
            Assert.DoesNotContain("Hacked", sent.Skills.TechnicalSkills);
        }

        [DbFact]
        public async Task Certifications_projects_and_top_level_achievements_are_always_empty_not_omitted()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            await Export(As(userId), resumeId);

            var sent = _factory.Documents.PdfRequests[^1];

            Assert.Empty(sent.Certifications);
            Assert.Empty(sent.Projects);
            Assert.Empty(sent.Achievements);
        }

        [DbFact]
        public async Task The_requested_template_is_the_one_sent_to_the_document_service()
        {
            var userId = NewUser();
            var resumeId = await NewResume(userId);

            await Export(As(userId), resumeId, template: "functional");

            Assert.Equal("functional", _factory.Documents.PdfRequests[^1].Template);
        }
    }
}
