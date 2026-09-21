using Joblink.Security;
using Joblink.Services.MyData;
using JobLinkv2.Services.MyData;
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

        public SavedJobsController(UserDataStore data)
        {
            _data = data;
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

            return _data.SaveJob(userId, jobId) == SaveJobOutcome.Saved
                ? Ok(new { userId, jobId })
                : NotFound(new { message = "That job doesn't exist." });
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
