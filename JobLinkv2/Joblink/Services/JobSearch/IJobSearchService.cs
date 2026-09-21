using System.Text.Json;

namespace Joblink.Services.JobSearch
{
    // The answer of a call to another service that JobLink depends on (JSearch today; the Python
    // JobLink-AI service is another). Either a value, or a status and a message that are fit to hand
    // straight to our own client - so a controller can map any failure the same way, and no upstream
    // error text, address or key ever reaches a browser.
    public sealed record UpstreamResult<T>
    {
        public T? Value { get; private init; }

        public int Status { get; private init; } = 200;

        public string? Message { get; private init; }

        public bool Ok => Message is null;

        public static UpstreamResult<T> Success(T value) => new() { Value = value };

        public static UpstreamResult<T> Failure(int status, string message) => new() { Status = status, Message = message };
    }

    // The job feed: JSearch (RapidAPI), called from the server so the key stays here, cached because
    // the plan allows only a few hundred calls a month.
    public interface IJobSearchService
    {
        // The jobs of one page of results as a JSON array, each saved to Job_Listings and tagged with
        // joblink_job_id and joblink_source. The query is already validated and trimmed, the page 1-10.
        Task<UpstreamResult<JsonElement>> SearchAsync(string query, int page, CancellationToken cancellationToken);

        // The full JSearch answer for one job.
        Task<UpstreamResult<JsonElement>> DetailsAsync(string jobId, CancellationToken cancellationToken);

        // The full JSearch answer for an estimated salary.
        Task<UpstreamResult<JsonElement>> SalaryAsync(string jobTitle, string location, CancellationToken cancellationToken);
    }
}
