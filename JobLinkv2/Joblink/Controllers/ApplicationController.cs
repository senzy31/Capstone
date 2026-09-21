using Joblink.Security;
using Joblink.Services.Tracker;
using JobLinkv2.Models;
using JobLinkv2.Services.Apply;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The job seeker's application tracker.
    //
    // Every action needs a login token, and the caller only ever sees or changes
    // their own applications. Creating applications for real jobs goes through
    // POST /api/jobs/{id}/apply (ApplyController), which is where the apply limit
    // and status rules live; the write actions here are limited to the tracker's
    // manual log so they can't be used to get around those rules.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class ApplicationController : ControllerBase
    {
        private readonly ApplicationTrackerService _tracker;

        public ApplicationController(ApplicationTrackerService tracker)
        {
            _tracker = tracker;
        }

        // The caller's own applications (this used to return everyone's).
        [HttpGet]
        public ActionResult GetAll()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(_tracker.List(userId));
        }

        [HttpGet("{id}")]
        public ActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return ToResult(_tracker.Get(userId, id));
        }

        [HttpGet("by-user/{userId}")]
        public ActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own applications." });

            return Ok(_tracker.List(callerId));
        }

        // "Log Application": record a job you applied to somewhere else.
        // The user always comes from the token; status must be a manual one.
        [HttpPost]
        public ActionResult Add([FromBody] LogApplicationRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "An application is required." });

            var result = _tracker.LogManual(userId, new ApplicationModel
            {
                JobId = request.JobId ?? 0,
                Status = request.Status,
                AppliedAt = request.AppliedAt
            });

            return result.IsOk ? Ok(true) : ToError(result.Outcome, result.Message);
        }

        // Only the status can change, and only among the tracker's own statuses.
        [HttpPut]
        public ActionResult Update([FromBody] ChangeApplicationStatusRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "An application is required." });

            var result = _tracker.ChangeStatus(userId, request.ApplicationId ?? 0, request.Status);

            return result.IsOk ? Ok(true) : ToError(result.Outcome, result.Message);
        }

        [HttpDelete]
        public ActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var result = _tracker.Withdraw(userId, id);

            return result.IsOk ? Ok(result.Value) : ToError(result.Outcome, result.Message);
        }

        private ActionResult ToResult<T>(TrackerResult<T> result) =>
            result.IsOk ? Ok(result.Value) : ToError(result.Outcome, result.Message);

        private ActionResult ToError(TrackerOutcome outcome, string? message) => outcome switch
        {
            TrackerOutcome.NotFound => NotFound(new { message = message ?? "Application not found." }),
            TrackerOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { message }),
            TrackerOutcome.Invalid => BadRequest(new { message }),
            _ => Conflict(new { message })
        };
    }
}
