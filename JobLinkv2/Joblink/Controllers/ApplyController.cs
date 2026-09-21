using Joblink.Security;
using JobLinkv2.Services.Apply;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The Apply button and its follow-up question ("Did you finish applying?").
    // Routes are spelled out because they don't follow api/[controller].
    [ApiController]
    [Authorize(Roles = "user")]
    public class ApplyController : ControllerBase
    {
        private readonly ApplyService _apply;

        public ApplyController(ApplyService apply)
        {
            _apply = apply;
        }

        // Internal job -> creates an application the employer receives.
        // External job -> records the click and returns the original posting's URL.
        [HttpPost("api/jobs/{jobId:int}/apply")]
        public IActionResult Apply(int jobId)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            switch (_apply.Apply(userId, jobId))
            {
                case InternalApplied applied:
                    return Ok(new
                    {
                        type = "internal",
                        applicationId = applied.ApplicationId,
                        status = applied.Status,
                        alreadyApplied = applied.AlreadyApplied
                    });

                case ExternalRedirect redirect:
                    return Ok(new
                    {
                        type = "external",
                        applicationId = redirect.ApplicationId,
                        redirectUrl = redirect.RedirectUrl,
                        publisher = redirect.Publisher,
                        status = redirect.Status
                    });

                case ApplyExpired expired:
                    return StatusCode(StatusCodes.Status410Gone, new
                    {
                        type = "external",
                        expired = true,
                        publisher = expired.Publisher,
                        message = "Sorry, this job posting has expired or been taken down, so it can't be applied to anymore."
                    });

                case ApplyRateLimited limited:
                    Response.Headers["Retry-After"] = limited.RetryAfterSeconds.ToString();

                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        message = $"You've reached the limit of {ApplyService.InternalApplicationLimit} applications per 24 hours. " +
                                  $"You can apply again in about {DescribeWait(limited.RetryAfterSeconds)}.",
                        retryAfterSeconds = limited.RetryAfterSeconds
                    });

                default:
                    return NotFound(new { message = "That job doesn't exist or has been removed." });
            }
        }

        // "Did you finish applying on {publisher}?" - answered from the modal or the tracker.
        [HttpPatch("api/applications/{id:int}/confirm-external")]
        public IActionResult ConfirmExternal(int id, [FromBody] ConfirmExternalRequest? body)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (body?.Applied is not bool applied)
                return BadRequest(new { message = "Send { \"applied\": true } or { \"applied\": false }." });

            switch (_apply.ConfirmExternal(userId, id, applied))
            {
                case ConfirmChanged changed:
                    return Ok(new
                    {
                        applicationId = changed.Application.ApplicationId,
                        status = changed.Application.Status,
                        confirmedAt = changed.Application.ConfirmedAt
                    });

                case ConfirmUnchanged unchanged:
                    return Ok(new
                    {
                        applicationId = unchanged.Application.ApplicationId,
                        status = unchanged.Application.Status,
                        confirmedAt = unchanged.Application.ConfirmedAt
                    });

                case ConfirmForbidden:
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "That application belongs to another user." });

                case ConfirmWrongStatus wrong:
                    return Conflict(new
                    {
                        message = "This application isn't waiting for confirmation.",
                        status = wrong.CurrentStatus
                    });

                default:
                    return NotFound(new { message = "Application not found." });
            }
        }

        private static string DescribeWait(int seconds)
        {
            if (seconds >= 3600)
            {
                var hours = (int)Math.Ceiling(seconds / 3600.0);
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }

            var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
    }

    public sealed class ConfirmExternalRequest
    {
        public bool? Applied { get; set; }
    }
}
