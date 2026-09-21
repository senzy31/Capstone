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
    }
}
