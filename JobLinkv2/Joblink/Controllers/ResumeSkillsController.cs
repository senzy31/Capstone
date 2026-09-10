using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResumeSkillsController : ControllerBase
    {
        ResumeSkillsServices resumeSkillsServices = new ResumeSkillsServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var resumeSkills = resumeSkillsServices.GetAll();
            return Ok(resumeSkills);
        }

        [HttpGet("{id}")]
        public ResumeSkillsModel GetById(int id)
        {
            return resumeSkillsServices.GetById(id);
        }

        [HttpGet("by-resume/{resumeId}")]
        public ActionResult GetByResumeId(int resumeId)
        {
            return Ok(resumeSkillsServices.GetByResumeId(resumeId));
        }

        [HttpPost]
        public bool Add(ResumeSkillsModel resumeSkills)
        {
            return resumeSkillsServices.Add(resumeSkills);
        }

        [HttpPut]
        public bool Update(ResumeSkillsModel resumeSkills)
        {
            return resumeSkillsServices.Update(resumeSkills);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return resumeSkillsServices.Delete(id);
        }

        // Resume_Skills has a composite key (resume_id, skill_id) - the plain
        // Delete(int) above can't target a row here, this is the real one.
        [HttpDelete("{resumeId}/{skillId}")]
        public IActionResult Remove(int resumeId, int skillId)
        {
            var removed = resumeSkillsServices.Remove(resumeId, skillId);

            if (!removed)
                return NotFound();

            return Ok(new { message = "Skill removed from resume" });
        }
    }
}
