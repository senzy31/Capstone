using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : ControllerBase
    {
        SkillsServices skillsServices = new SkillsServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var skills = skillsServices.GetAll();
            return Ok(skills);
        }

        [HttpGet("{id}")]
        public SkillsModel GetById(int id)
        {
            return skillsServices.GetById(id);
        }

        [HttpPost]
        public bool Add(SkillsModel skills)
        {
            return skillsServices.Add(skills);
        }

        [HttpPut]
        public bool Update(SkillsModel skills)
        {
            return skillsServices.Update(skills);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return skillsServices.Delete(id);
        }
    }
}
