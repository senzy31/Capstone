using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        NotificationServices notificationServices = new NotificationServices();

        [HttpGet]
        public ActionResult GetAll()
        {
            var notif = notificationServices.GetAll();
            return Ok(notif);
        }

        [HttpGet("{id}")]
        public NotificationModel GetById(int id)
        {
            return notificationServices.GetById(id);
        }

        [HttpPost]
        public bool Add(NotificationModel notif)
        {
            return notificationServices.Add(notif);
        }

        [HttpPut]
        public bool Update(NotificationModel notif)
        {
            return notificationServices.Update(notif);
        }

        [HttpDelete]
        public bool Delete(int id)
        {
            return notificationServices.Delete(id);
        }
    }
}
