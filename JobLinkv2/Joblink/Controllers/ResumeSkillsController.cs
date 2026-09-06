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
    }
}
