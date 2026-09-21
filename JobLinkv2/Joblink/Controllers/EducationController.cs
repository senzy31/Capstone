using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Education entries on the caller's own resumes. An entry is reached through
    // its resume, so someone else's resume (or entry) is a 404, and an entry can't
    // be moved to a resume that isn't yours.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class EducationController : ControllerBase
    {
        private readonly ResumeDataStore _data;

        public EducationController(ResumeDataStore data)
        {
            _data = data;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var entry = _data.GetEducation(userId, id);

            return entry is null ? NotFound(new { message = "Education entry not found." }) : Ok(entry);
        }

        [HttpGet("by-resume/{resumeId}")]
        public IActionResult GetByResumeId(int resumeId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var entries = _data.ListEducation(userId, resumeId);

            return entries is null ? NotFound(new { message = "Resume not found." }) : Ok(entries);
        }

        // Adds an entry to one of your resumes (ResumeId in the body).
        [HttpPost]
        public IActionResult Add([FromBody] SaveEducationRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ResumeId is not int resumeId)
                return BadRequest(new { message = "A resume id is required.", code = "invalid" });

            var created = _data.AddEducation(userId, resumeId, request.ToFields());

            return created is null ? NotFound(new { message = "Resume not found." }) : Ok(created);
        }

        // Saves an entry of yours (EducationId in the body). ResumeId is ignored.
        [HttpPut]
        public IActionResult Update([FromBody] SaveEducationRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.EducationId is not int educationId)
                return BadRequest(new { message = "An education id is required.", code = "invalid" });

            return _data.UpdateEducation(userId, educationId, request.ToFields())
                ? Ok(_data.GetEducation(userId, educationId))
                : NotFound(new { message = "Education entry not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteEducation(userId, id)
                ? Ok(new { message = "Education entry deleted." })
                : NotFound(new { message = "Education entry not found." });
        }
    }
}
