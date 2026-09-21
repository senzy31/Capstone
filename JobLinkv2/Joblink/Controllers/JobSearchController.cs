using Joblink.Services.JobSearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Server-side proxy for the JSearch job feed (RapidAPI): what is asked, cached and imported lives in
    // IJobSearchService (see RapidApiJobSearchService); this checks the request and shapes the answer.
    //
    // Every search spends part of that plan's small monthly allowance, so it needs a
    // login (any role): an anonymous caller could use it all up.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class JobSearchController : ControllerBase
    {
        private readonly IJobSearchService _jobs;

        public JobSearchController(IJobSearchService jobs)
        {
            _jobs = jobs;
        }

        // GET api/JobSearch/search?query=developer%20manila&page=1
        // Returns { status, data: [ ...jobs ] }. Every job is saved to Job_Listings
        // and carries joblink_job_id (its saved id) for POST /api/jobs/{id}/apply.
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "query is required" });

            if (query.Length > 200)
                return BadRequest(new { message = "query is too long" });

            page = Math.Clamp(page, 1, 10);

            var result = await _jobs.SearchAsync(query.Trim(), page, HttpContext.RequestAborted);

            return result.Ok
                ? Ok(new { status = "OK", data = result.Value })
                : StatusCode(result.Status, new { message = result.Message });
        }

        // GET api/JobSearch/details?jobId=...
        [HttpGet("details")]
        public async Task<IActionResult> Details([FromQuery] string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 1000)
                return BadRequest(new { message = "a valid jobId is required" });

            var result = await _jobs.DetailsAsync(jobId, HttpContext.RequestAborted);

            return result.Ok ? Ok(result.Value) : StatusCode(result.Status, new { message = result.Message });
        }

        // GET api/JobSearch/salary?jobTitle=...&location=...
        [HttpGet("salary")]
        public async Task<IActionResult> Salary([FromQuery] string? jobTitle, [FromQuery] string? location)
        {
            if (string.IsNullOrWhiteSpace(jobTitle) || string.IsNullOrWhiteSpace(location))
                return BadRequest(new { message = "jobTitle and location are required" });

            if (jobTitle.Length > 200 || location.Length > 200)
                return BadRequest(new { message = "jobTitle or location is too long" });

            var result = await _jobs.SalaryAsync(jobTitle.Trim(), location.Trim(), HttpContext.RequestAborted);

            return result.Ok ? Ok(result.Value) : StatusCode(result.Status, new { message = result.Message });
        }
    }
}
