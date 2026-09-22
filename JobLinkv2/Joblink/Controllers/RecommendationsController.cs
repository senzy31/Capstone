using Joblink.Security;
using Joblink.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The logged-in job seeker's recommended jobs, scored against their own resume and preferences.
    // The only thing a caller chooses is the page: who they are comes from the login token, what they are
    // scored against comes from the database, and how much of each score they see comes from their plan.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class RecommendationsController : ControllerBase
    {
        private readonly RecommendationService _recommendations;

        public RecommendationsController(RecommendationService recommendations)
        {
            _recommendations = recommendations;
        }

        // GET api/Recommendations?page=1
        //   { status, query, page, skillCount, hasPreferences, detailed, data: [ ...jobs, each with joblink_match ] }
        // A job seeker with no skills gets an empty list and skillCount 0 (and no search is made).
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int page = 1)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var outcome = await _recommendations.GetAsync(userId, Math.Clamp(page, 1, 10), HttpContext.RequestAborted);

            return outcome switch
            {
                Recommended r => Ok(new { status = "OK", query = r.Query, page = r.Page, skillCount = r.SkillCount, hasPreferences = r.HasPreferences, detailed = r.Detailed, data = r.Jobs }),
                RecommendationFailed f => StatusCode(f.Status, new { message = f.Message }),
                _ => StatusCode(500)
            };
        }

        // GET api/Recommendations/search?q=...&page=1&workSetup=&location=&minSalary=&maxSalary=&jobType=&minScore=
        // The Jobs page: the caller's own search text and filters, not their resume. Internal jobs
        // matching them come first (best match, then newest), external jobs after - each job still
        // scored and shown as much of that score as the plan allows, exactly like GET api/Recommendations.
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] string? workSetup = null,
            [FromQuery] string? location = null,
            [FromQuery] double? minSalary = null,
            [FromQuery] double? maxSalary = null,
            [FromQuery] string? jobType = null,
            [FromQuery] int? minScore = null)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var filters = new SearchFilters(workSetup, location, minSalary, maxSalary, jobType, minScore);

            var outcome = await _recommendations.SearchAsync(userId, q, Math.Clamp(page, 1, 10), filters, HttpContext.RequestAborted);

            return outcome switch
            {
                Recommended r => Ok(new { status = "OK", query = r.Query, page = r.Page, skillCount = r.SkillCount, hasPreferences = r.HasPreferences, detailed = r.Detailed, data = r.Jobs }),
                RecommendationFailed f => StatusCode(f.Status, new { message = f.Message }),
                _ => StatusCode(500)
            };
        }
    }
}
