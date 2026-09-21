using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The skills on the caller's own resumes. A link is (resume, skill); it is
    // only ever reached through a resume you own. The old "list all", "by id",
    // PUT and delete-by-id actions are gone: the table has a two-part key, so
    // they never worked, and they were open to anyone.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class ResumeSkillsController : ControllerBase
    {
        private readonly ResumeDataStore _data;

        public ResumeSkillsController(ResumeDataStore data)
        {
            _data = data;
        }

        [HttpGet("by-resume/{resumeId}")]
        public IActionResult GetByResumeId(int resumeId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var links = _data.ListResumeSkills(userId, resumeId);

            return links is null ? NotFound(new { message = "Resume not found." }) : Ok(links);
        }

        // Puts a skill on one of your resumes. Adding one that's there is fine.
        [HttpPost]
        public IActionResult Add([FromBody] AddResumeSkillRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ResumeId is not int resumeId || request.SkillId is not int skillId)
                return BadRequest(new { message = "A resume id and a skill id are required.", code = "invalid" });

            return _data.AddResumeSkill(userId, resumeId, skillId) switch
            {
                AddSkillOutcome.Added => Ok(new { resumeId, skillId }),
                AddSkillOutcome.SkillNotFound => BadRequest(new { message = "That skill doesn't exist.", code = "invalid" }),
                _ => NotFound(new { message = "Resume not found." })
            };
        }

        [HttpDelete("{resumeId}/{skillId}")]
        public IActionResult Remove(int resumeId, int skillId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.RemoveResumeSkill(userId, resumeId, skillId)
                ? Ok(new { message = "Skill removed from resume" })
                : NotFound(new { message = "Skill not found on that resume." });
        }
    }
}
