using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JoblistingController : ControllerBase
    {
        JobListingServices joblistingServices = new JobListingServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var joblist = joblistingServices.GetAll();
            return Ok(joblist);
        }

        [HttpGet("{id}")]
        public JoblistingModel GetById(int id)
        {
            return joblistingServices.GetById(id);
        }

        [HttpPost]
        public bool Add(JoblistingModel joblist)
        {
            return joblistingServices.Add(joblist);
        }

        [HttpPut]
        public bool Update(JoblistingModel joblist)
        {
            return joblistingServices.Update(joblist);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return joblistingServices.Delete(id);
        }
    }
}
