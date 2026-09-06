using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResumeController : ControllerBase
    {
        ResumeServices resumeServices = new ResumeServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var resume = resumeServices.GetAll();
            return Ok(resume);
        }

        [HttpGet("{id}")]
        public ResumeModel GetById(int id)
        {
            return resumeServices.GetById(id);
        }

        [HttpPost]
        public bool Add(ResumeModel resume)
        {
            return resumeServices.Add(resume);
        }

        [HttpPut]
        public bool Update(ResumeModel resume)
        {
            return resumeServices.Update(resume);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return resumeServices.Delete(id);
        }
    }
}
