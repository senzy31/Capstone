using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Work-experience entries on the caller's own resumes: same rules as Education.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class ExperienceController : ControllerBase
    {
        private readonly ResumeDataStore _data;

        public ExperienceController(ResumeDataStore data)
        {
            _data = data;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var entry = _data.GetExperience(userId, id);

            return entry is null ? NotFound(new { message = "Experience entry not found." }) : Ok(entry);
        }

        [HttpGet("by-resume/{resumeId}")]
        public IActionResult GetByResumeId(int resumeId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var entries = _data.ListExperience(userId, resumeId);

            return entries is null ? NotFound(new { message = "Resume not found." }) : Ok(entries);
        }

        // Adds an entry to one of your resumes (ResumeId in the body).
        [HttpPost]
        public IActionResult Add([FromBody] SaveExperienceRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ResumeId is not int resumeId)
                return BadRequest(new { message = "A resume id is required.", code = "invalid" });

            var created = _data.AddExperience(userId, resumeId, request.ToFields());

            return created is null ? NotFound(new { message = "Resume not found." }) : Ok(created);
        }

        // Saves an entry of yours (ExperienceId in the body). ResumeId is ignored.
        [HttpPut]
        public IActionResult Update([FromBody] SaveExperienceRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ExperienceId is not int experienceId)
                return BadRequest(new { message = "An experience id is required.", code = "invalid" });

            return _data.UpdateExperience(userId, experienceId, request.ToFields())
                ? Ok(_data.GetExperience(userId, experienceId))
                : NotFound(new { message = "Experience entry not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteExperience(userId, id)
                ? Ok(new { message = "Experience entry deleted." })
                : NotFound(new { message = "Experience entry not found." });
        }
    }
}
