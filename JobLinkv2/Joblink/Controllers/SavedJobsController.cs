using Joblink.Security;
using Joblink.Services.MyData;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The jobs a job seeker has saved for later - their own list only. A saved job is
    // (you, job); the old routes by id, PUT and delete-by-id never worked on that
    // two-part key and are gone.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class SavedJobsController : ControllerBase
    {
        private readonly UserDataStore _data;
        private readonly IPlanReader _plans;

        public SavedJobsController(UserDataStore data, IPlanReader plans)
        {
            _data = data;
            _plans = plans;
        }

        // Your saved jobs (this used to return everyone's).
        [HttpGet]
        public IActionResult GetAll()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(_data.ListSavedJobs(userId));
        }

        // Saves a job for you. Saving one that's already saved is fine.
        [HttpPost]
        public IActionResult Add([FromBody] SaveJobRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.JobId is not int jobId)
                return BadRequest(new { message = "A job id is required.", code = "invalid" });

            // Free plans can keep 10 saved jobs, Premium any number. Saving one that is already
            // saved is always fine.
            var premium = _plans.IsPremium(userId);
            var limit = PlanLimits.For(premium).SavedJobs;

            return _data.SaveJob(userId, jobId, limit) switch
            {
                SaveJobOutcome.Saved => Ok(new { userId, jobId }),
                SaveJobOutcome.LimitReached => PlanResponses.LimitReached(this, premium, "savedJobs", limit ?? 0,
                    freeMessage: $"Free accounts can save up to {limit} jobs. Upgrade to Premium for unlimited saved jobs.",
                    premiumMessage: "You've reached your saved jobs limit."),
                _ => NotFound(new { message = "That job doesn't exist." })
            };
        }

        [HttpDelete("{jobId}")]
        public IActionResult Remove(int jobId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.UnsaveJob(userId, jobId)
                ? Ok(new { message = "Job removed from your saved jobs." })
                : NotFound(new { message = "That job isn't in your saved jobs." });
        }
    }
}
