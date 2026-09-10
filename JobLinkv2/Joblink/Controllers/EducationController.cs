using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EducationController : ControllerBase
    {
        EducationServices educationServices = new EducationServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var education = educationServices.GetAll();
            return Ok(education);
        }

        [HttpGet("{id}")]
        public EducationModel GetById(int id)
        {
            return educationServices.GetById(id);
        }

        [HttpGet("by-resume/{resumeId}")]
        public ActionResult GetByResumeId(int resumeId)
        {
            return Ok(educationServices.GetByResumeId(resumeId));
        }

        [HttpPost]
        public bool Add(EducationModel education)
        {
            return educationServices.Add(education);
        }

        [HttpPut]
        public bool Update(EducationModel education)
        {
            return educationServices.Update(education);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return educationServices.Delete(id);
        }
    }
}
