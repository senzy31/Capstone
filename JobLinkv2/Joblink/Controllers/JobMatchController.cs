using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobMatchController : ControllerBase
    {
        JobMatchServices jobmatchservices = new JobMatchServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var jobMatches = jobmatchservices.GetAll();
            return Ok(jobMatches);
        }

        [HttpGet("{id}")]
        public JobMatchModel GetById(int id)
        {
            return jobmatchservices.GetById(id);
        }

        [HttpPost]
        public bool Add(JobMatchModel jobMatches)
        {
            return jobmatchservices.Add(jobMatches);
        }

        [HttpPut]
        public bool Update(JobMatchModel jobMatches)
        {
            return jobmatchservices.Update(jobMatches);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return jobmatchservices.Delete(id);
        }
    }
}