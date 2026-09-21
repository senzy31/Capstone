using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // A job seeker's own resumes. Every action is scoped to the caller: someone
    // else's resume is a 404, and asking for another user's list is a 403.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class ResumeController : ControllerBase
    {
        private readonly ResumeDataStore _data;
        private readonly TimeProvider _time;
        private readonly IPlanReader _plans;

        public ResumeController(ResumeDataStore data, TimeProvider time, IPlanReader plans)
        {
            _data = data;
            _time = time;
            _plans = plans;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var resume = _data.GetResume(userId, id);

            return resume is null ? NotFound(new { message = "Resume not found." }) : Ok(resume);
        }

        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own resumes." });

            return Ok(_data.ListResumes(callerId));
        }

        // Creates a resume for you (the owner is never taken from the request). Free plans keep
        // one saved resume, Premium up to ten: past that it is a 403, decided here on the server
        // from the plan in the database - hiding a button is not the limit.
        [HttpPost]
        public IActionResult Add([FromBody] CreateResumeRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var premium = _plans.IsPremium(userId);
            var limit = PlanLimits.For(premium).ResumeVersions;

            var created = _data.TryAddResume(
                userId,
                string.IsNullOrWhiteSpace(request?.Title) ? "My Resume" : request!.Title!.Trim(),
                string.IsNullOrWhiteSpace(request?.TemplateType) ? "standard" : request!.TemplateType!.Trim(),
                _time.GetLocalNow().DateTime,
                limit);

            return created is null
                ? PlanResponses.LimitReached(this, premium, "resumeVersions", limit,
                    freeMessage: $"Free accounts can save {limit} resume. Upgrade to Premium to save up to {PlanLimits.Premium.ResumeVersions}.",
                    premiumMessage: $"You've reached the limit of {limit} saved resumes. Delete one to make room.")
                : Ok(created);
        }

        // Saves the title and content of one of your resumes.
        [HttpPut]
        public IActionResult Update([FromBody] UpdateResumeRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ResumeId is not int resumeId)
                return BadRequest(new { message = "A resume id is required.", code = "invalid" });

            var saved = _data.UpdateResume(
                userId,
                resumeId,
                string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                string.IsNullOrWhiteSpace(request.TemplateType) ? null : request.TemplateType.Trim(),
                request.AiGeneratedContent);

            return saved
                ? Ok(_data.GetResume(userId, resumeId))
                : NotFound(new { message = "Resume not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteResume(userId, id)
                ? Ok(new { message = "Resume deleted." })
                : NotFound(new { message = "Resume not found." });
        }
    }
}
