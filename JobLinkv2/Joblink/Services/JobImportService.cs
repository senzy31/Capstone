using System.Text.Json;
using System.Text.Json.Nodes;
using JobLinkv2.Services.Apply;

namespace Joblink.Services
{
    // Saves the jobs a JSearch search returns into Job_Listings (source 'External')
    // and tags each result with the id of its saved row, so the Apply button can
    // call POST /api/jobs/{jobId}/apply. Importing a job that is already saved
    // just refreshes it.
    public sealed class JobImportService
    {
        private readonly IApplyStore _store;
        private readonly ILogger<JobImportService> _logger;

        public JobImportService(IApplyStore store, ILogger<JobImportService> logger)
        {
            _store = store;
            _logger = logger;
        }

        // Returns the same jobs with two extra fields on each:
        //   joblink_job_id  - the Job_Listings id
        //   joblink_source  - "External"
        // If the database is unavailable the jobs come back untagged (Complete =
        // false, so the caller doesn't cache them) rather than failing the search.
        public (JsonArray Jobs, bool Complete) ImportAndTag(JsonElement jobs)
        {
            var tagged = new JsonArray();
            var databaseFailed = false;

            foreach (var job in jobs.EnumerateArray())
            {
                var node = JsonNode.Parse(job.GetRawText())!.AsObject();

                if (!databaseFailed && JSearchListingMapper.TryMap(job, out var listing) && listing != null)
                {
                    try
                    {
                        node["joblink_job_id"] = _store.UpsertExternalListing(listing);
                        node["joblink_source"] = ListingSources.External;
                    }
                    catch (Exception ex)
                    {
                        // Don't retry every job against a database that just failed.
                        databaseFailed = true;

                        _logger.LogWarning(ex, "Couldn't save imported jobs; returning search results untagged.");
                    }
                }

                tagged.Add(node);
            }

            return (tagged, !databaseFailed);
        }
    }
}
