using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Joblink.Services.Resume;
using JobLinkv2.Services.Resumes.Export;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Joblink.Tests
{
    // The JobLink-AI client: what it sends, how the filename and content type come back, and how
    // each way of failing is reported. No network - the far end is a stub, the same pattern as
    // RapidApiJobSearchServiceTests.
    public class PythonResumeServiceTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new();

            public List<string> RequestBodies { get; } = new();

            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

                return Respond(request);
            }
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public StubFactory(HttpMessageHandler handler) => _handler = handler;

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private readonly StubHandler _handler = new();

        private static readonly ResumeDataDto Sample = new(
            new PersonalInfoDto("Maria Santos", "maria@example.com", null, null, null, null),
            new CareerInfoDto(null, null, null),
            Array.Empty<EducationDto>(),
            Array.Empty<ExperienceDto>(),
            new SkillsDto(new[] { "React" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            "harvard");

        private static HttpResponseMessage FileResponse(byte[] bytes, string contentType, string? filename = "resume.pdf")
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            if (filename != null)
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = filename };

            return response;
        }

        private PythonResumeService Service(string? baseUrl = null)
        {
            var settings = new Dictionary<string, string?> { ["JobLinkAi:BaseUrl"] = baseUrl };

            return new PythonResumeService(
                new StubFactory(_handler),
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PythonResumeService>.Instance);
        }

        // ----- what it asks for -------------------------------------------------------------------------

        [Fact]
        public async Task A_pdf_request_posts_to_generate_pdf_with_the_default_local_address()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1, 2, 3 }, "application/pdf");

            await Service().GeneratePdfAsync(Sample, default);

            var request = Assert.Single(_handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://127.0.0.1:8001/api/resume/generate-pdf", request.RequestUri!.ToString());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        }

        [Fact]
        public async Task A_docx_request_posts_to_generate_docx()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            await Service().GenerateDocxAsync(Sample, default);

            Assert.Equal("http://127.0.0.1:8001/api/resume/generate-docx", _handler.Requests.Single().RequestUri!.ToString());
        }

        [Theory]
        [InlineData("http://127.0.0.1:9000", "http://127.0.0.1:9000/api/resume/generate-pdf")]
        [InlineData("http://127.0.0.1:9000/", "http://127.0.0.1:9000/api/resume/generate-pdf")]
        [InlineData("  http://fake.example/svc/  ", "http://fake.example/svc/api/resume/generate-pdf")]
        [InlineData("", "http://127.0.0.1:8001/api/resume/generate-pdf")]
        [InlineData(null, "http://127.0.0.1:8001/api/resume/generate-pdf")]
        public async Task The_address_can_be_pointed_elsewhere_by_configuration(string? baseUrl, string expectedUrl)
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/pdf");

            await Service(baseUrl).GeneratePdfAsync(Sample, default);

            Assert.Equal(expectedUrl, _handler.Requests.Single().RequestUri!.ToString());
        }

        [Fact]
        public async Task The_body_is_the_resume_wrapped_in_one_object_in_snake_case_field_names()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/pdf");

            await Service().GeneratePdfAsync(Sample, default);

            using var body = JsonDocument.Parse(_handler.RequestBodies.Single());
            var resume = body.RootElement.GetProperty("resume");

            Assert.Equal("Maria Santos", resume.GetProperty("personal_info").GetProperty("full_name").GetString());
            Assert.Equal("maria@example.com", resume.GetProperty("personal_info").GetProperty("email").GetString());
            Assert.Equal("harvard", resume.GetProperty("template").GetString());
            Assert.Equal(new[] { "React" }, resume.GetProperty("skills").GetProperty("technical_skills").EnumerateArray().Select(e => e.GetString()));
            Assert.Empty(resume.GetProperty("certifications").EnumerateArray());
        }

        [Fact]
        public async Task Nothing_but_resume_is_sent_no_separate_top_level_template()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/pdf");

            await Service().GeneratePdfAsync(Sample, default);

            using var body = JsonDocument.Parse(_handler.RequestBodies.Single());
            Assert.Equal(new[] { "resume" }, body.RootElement.EnumerateObject().Select(p => p.Name));
        }

        // ----- what it returns -----------------------------------------------------------------------------

        [Fact]
        public async Task A_successful_answer_carries_the_bytes_content_type_and_filename()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf", "Maria_Santos_Resume.pdf");

            var result = await Service().GeneratePdfAsync(Sample, default);

            Assert.True(result.Ok);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, result.Value!.Bytes);
            Assert.Equal("application/pdf", result.Value.ContentType);
            Assert.Equal("Maria_Santos_Resume.pdf", result.Value.FileName);
        }

        [Fact]
        public async Task No_filename_from_the_service_falls_back_to_a_generic_one_per_format()
        {
            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/pdf", filename: null);

            var pdf = await Service().GeneratePdfAsync(Sample, default);
            Assert.Equal("resume.pdf", pdf.Value!.FileName);

            _handler.Respond = _ => FileResponse(new byte[] { 1 }, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", filename: null);
            var docx = await Service().GenerateDocxAsync(Sample, default);
            Assert.Equal("resume.docx", docx.Value!.FileName);
        }

        [Fact]
        public async Task An_empty_body_is_treated_as_a_failure_even_with_a_200()
        {
            _handler.Respond = _ => FileResponse(Array.Empty<byte>(), "application/pdf");

            var result = await Service().GeneratePdfAsync(Sample, default);

            Assert.False(result.Ok);
            Assert.Equal(502, result.Status);
        }

        // ----- each way of failing -----------------------------------------------------------------------------

        [Theory]
        [InlineData(422)]
        [InlineData(500)]
        [InlineData(404)]
        public async Task A_non_success_status_is_a_clean_502_never_the_services_own_error_text(int upstream)
        {
            _handler.Respond = _ => new HttpResponseMessage((HttpStatusCode)upstream)
            { Content = new StringContent("{\"detail\":[{\"loc\":[\"resume\",\"personal_info\",\"email\"],\"msg\":\"internal validation detail\"}]}", Encoding.UTF8, "application/json") };

            var result = await Service().GeneratePdfAsync(Sample, default);

            Assert.False(result.Ok);
            Assert.Equal(502, result.Status);
            Assert.Equal("Couldn't generate that document right now.", result.Message);
            Assert.DoesNotContain("internal validation detail", result.Message);
        }

        [Fact]
        public async Task A_service_that_cannot_be_reached_is_reported_as_unavailable()
        {
            _handler.Respond = _ => throw new HttpRequestException("connection refused to 10.0.0.5");

            var result = await Service().GeneratePdfAsync(Sample, default);

            Assert.Equal(503, result.Status);
            Assert.Equal("The resume document service is not available right now.", result.Message);
            Assert.DoesNotContain("10.0.0.5", result.Message);
        }

        [Fact]
        public async Task A_service_that_takes_too_long_is_a_gateway_timeout()
        {
            _handler.Respond = _ => throw new TaskCanceledException("timed out", new TimeoutException());

            var result = await Service().GeneratePdfAsync(Sample, default);

            Assert.Equal(504, result.Status);
            Assert.Equal("The resume document service took too long to respond.", result.Message);
        }

        [Fact]
        public async Task A_caller_that_gave_up_is_not_told_the_service_timed_out()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            _handler.Respond = _ => throw new TaskCanceledException();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GeneratePdfAsync(Sample, cancelled.Token));
        }
    }
}
