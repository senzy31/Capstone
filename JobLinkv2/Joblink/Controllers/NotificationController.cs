using Joblink.Security;
using Joblink.Services.MyData;
using JobLinkv2.Services.MyData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The logged-in user's own notifications (employers get them too, when someone
    // applies to their job). Notifications are created by the server - there is no
    // POST - and the only change a user can make is to mark one read or delete it.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly UserDataStore _data;

        public NotificationController(UserDataStore data)
        {
            _data = data;
        }

        // Your notifications, newest first (this used to return everyone's).
        [HttpGet]
        public IActionResult GetAll()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(_data.ListNotifications(userId));
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var notification = _data.GetNotification(userId, id);

            return notification is null ? NotFound(new { message = "Notification not found." }) : Ok(notification);
        }

        // Marks one of your notifications read (or unread). Nothing else can change.
        [HttpPut]
        public IActionResult Update([FromBody] UpdateNotificationRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.NotificationId is not int notificationId)
                return BadRequest(new { message = "A notification id is required.", code = "invalid" });

            return _data.SetNotificationRead(userId, notificationId, request.IsRead)
                ? Ok(_data.GetNotification(userId, notificationId))
                : NotFound(new { message = "Notification not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteNotification(userId, id)
                ? Ok(new { message = "Notification deleted." })
                : NotFound(new { message = "Notification not found." });
        }
    }
}
