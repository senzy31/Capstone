using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExperienceController : ControllerBase
    {
        ExperienceServices experienceServices = new ExperienceServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var exp = experienceServices.GetAll();
            return Ok(exp);
        }

        [HttpGet("{id}")]
        public ExperienceModel GetById(int id)
        {
            return experienceServices.GetById(id);
        }

        [HttpPost]
        public bool Add(ExperienceModel exp)
        {
            return experienceServices.Add(exp);
        }

        [HttpPut]
        public bool Update(ExperienceModel exp)
        {
            return experienceServices.Update(exp);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return experienceServices.Delete(id);
        }
    }
}
