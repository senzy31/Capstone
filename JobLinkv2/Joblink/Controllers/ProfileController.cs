using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProfileController : ControllerBase
    {
        ProfileServices profileServices = new ProfileServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var profile = profileServices.GetAll();
            return Ok(profile);
        }

        [HttpGet("{id}")]
        public ProfileModel GetById(int id)
        {
            return profileServices.GetById(id);
        }

        [HttpPost]
        public bool Add(ProfileModel profile)
        {
            return profileServices.Add(profile);
        }

        [HttpPut]
        public bool Update(ProfileModel profile)
        {
            return profileServices.Update(profile);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return profileServices.Delete(id);
        }
    }
}
