using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ApplicationController : ControllerBase
    {
        ApplicationServices applicationServices = new ApplicationServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var application = applicationServices.GetAll();
            return Ok(application);
        }

        [HttpGet("{id}")]
        public ApplicationModel GetById(int id)
        {
            return applicationServices.GetById(id);
        }

        [HttpPost]
        public bool Add(ApplicationModel application)
        {
            return applicationServices.Add(application);
        }

        [HttpPut]
        public bool Update(ApplicationModel application)
        {
            return applicationServices.Update(application);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return applicationServices.Delete(id);
        }
    }
}
