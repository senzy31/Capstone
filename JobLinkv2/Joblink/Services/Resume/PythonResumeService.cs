using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Joblink.Services.JobSearch;
using JobLinkv2.Services.Resumes.Export;

namespace Joblink.Services.Resume
{
    // Calls JobLink-AI (Python, JobLink-AI/app/main.py) to turn a resume into a PDF or DOCX. Local
    // service, no key: "JobLinkAi:BaseUrl" (default http://127.0.0.1:8001) is the only setting, the
    // same override pattern RapidApiJobSearchService uses for the live checks' fake JSearch.
    public sealed class PythonResumeService : IResumeDocumentService
    {
        private const string DefaultBaseUrl = "http://127.0.0.1:8001";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PythonResumeService> _logger;
        private readonly string _baseUrl;

        public PythonResumeService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<PythonResumeService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            var baseUrl = configuration["JobLinkAi:BaseUrl"];

            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        }

        public Task<UpstreamResult<GeneratedDocument>> GeneratePdfAsync(ResumeDataDto resume, CancellationToken cancellationToken) =>
            GenerateAsync("/api/resume/generate-pdf", resume, "application/pdf", "pdf", cancellationToken);

        public Task<UpstreamResult<GeneratedDocument>> GenerateDocxAsync(ResumeDataDto resume, CancellationToken cancellationToken) =>
            GenerateAsync(
                "/api/resume/generate-docx", resume,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx",
                cancellationToken);

        private async Task<UpstreamResult<GeneratedDocument>> GenerateAsync(
            string path, ResumeDataDto resume, string defaultContentType, string extension, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = RequestTimeout;

            HttpResponseMessage response;

            try
            {
                response = await client.PostAsJsonAsync(
                    $"{_baseUrl}{path}", new DocumentGenerationRequestDto(resume), JsonOptions, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return UpstreamResult<GeneratedDocument>.Failure(504, "The resume document service took too long to respond.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Could not reach JobLink-AI at {BaseUrl}.", _baseUrl);

                return UpstreamResult<GeneratedDocument>.Failure(503, "The resume document service is not available right now.");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.LogWarning("JobLink-AI {Path} returned {Status}: {Body}", path, (int)response.StatusCode, Truncate(body));

                    // A validation error (422) is our own mapping's fault, not the caller's - still
                    // reported as a server problem, never with the service's internal detail text.
                    return UpstreamResult<GeneratedDocument>.Failure(502, "Couldn't generate that document right now.");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (bytes.Length == 0)
                    return UpstreamResult<GeneratedDocument>.Failure(502, "Couldn't generate that document right now.");

                var contentType = response.Content.Headers.ContentType?.ToString() ?? defaultContentType;
                var fileName = FileNameFrom(response.Content.Headers.ContentDisposition) ?? $"resume.{extension}";

                return UpstreamResult<GeneratedDocument>.Success(new GeneratedDocument(bytes, contentType, fileName));
            }
        }

        private static string? FileNameFrom(ContentDispositionHeaderValue? header) =>
            string.IsNullOrWhiteSpace(header?.FileNameStar) ? Trim(header?.FileName) : Trim(header.FileNameStar);

        private static string? Trim(string? fileName) =>
            string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim('"');

        private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "...";
    }
}
