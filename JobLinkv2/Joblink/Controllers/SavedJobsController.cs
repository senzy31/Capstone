using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SavedJobsController : ControllerBase
    {
        SavedJobsServices savedjobsController = new SavedJobsServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var savedJobs = savedjobsController.GetAll();
            return Ok(savedJobs);
        }

        [HttpGet("{id}")]
        public SavedJobsModel GetById(int id)
        {
            return savedjobsController.GetById(id);
        }

        [HttpPost]
        public bool Add(SavedJobsModel savedJobs)
        {
            return savedjobsController.Add(savedJobs);
        }

        [HttpPut]
        public bool Update(SavedJobsModel savedJobs)
        {
            return savedjobsController.Update(savedJobs);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return savedjobsController.Delete(id);
        }
    }
}
