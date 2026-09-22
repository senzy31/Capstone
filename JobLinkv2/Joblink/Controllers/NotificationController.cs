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
        private const int PageSize = 20;

        private readonly UserDataStore _data;

        public NotificationController(UserDataStore data)
        {
            _data = data;
        }

        // Your notifications, newest first, one page at a time - and, on the same request, the
        // total count and how many are unread (the bell dropdown needs both without asking twice).
        [HttpGet]
        public IActionResult GetAll([FromQuery] int page = 1)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var result = _data.ListNotificationsPage(userId, Math.Max(page, 1), PageSize);

            return Ok(new { data = result.Items, page = Math.Max(page, 1), pageSize = PageSize, totalCount = result.TotalCount, unreadCount = result.UnreadCount });
        }

        // Polled every 30s for the bell's badge - cheap enough on its own that the page doesn't
        // have to fetch (and re-render) the whole list just to know whether the count changed.
        [HttpGet("unread-count")]
        public IActionResult UnreadCount()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(new { count = _data.UnreadNotificationCount(userId) });
        }

        [HttpGet("{id:int}")]
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

        [HttpPatch("{id:int}/read")]
        public IActionResult MarkRead(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.SetNotificationRead(userId, id, true)
                ? Ok(_data.GetNotification(userId, id))
                : NotFound(new { message = "Notification not found." });
        }

        [HttpPatch("read-all")]
        public IActionResult MarkAllRead()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(new { updated = _data.MarkAllNotificationsRead(userId) });
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
