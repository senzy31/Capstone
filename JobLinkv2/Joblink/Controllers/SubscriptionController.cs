using Joblink.Security;
using Joblink.Services.Subscriptions;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // A job seeker's plan (Free / Premium). Employers have no plans: they get a 403.
    //
    // Payments are SIMULATED - upgrading grants Premium and charges nothing. The plan is
    // read from the database on every request, never from the login token.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class SubscriptionController : ControllerBase
    {
        private readonly SubscriptionService _plans;
        private readonly IUsageReader _usage;
        private readonly SubscriptionOptions _options;

        public SubscriptionController(
            SubscriptionService plans,
            IUsageReader usage,
            SubscriptionOptions options)
        {
            _plans = plans;
            _usage = usage;
            _options = options;
        }

        // Your plan, what it allows, and how much you have used.
        [HttpGet]
        public IActionResult Get()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            // Checked here, not in GetStatus itself - see SubscriptionService.CheckExpiryNotifications.
            _plans.CheckExpiryNotifications(userId);

            return Ok(Describe(userId, _plans.GetStatus(userId)));
        }

        // Simulated checkout. Premium lasts one / three / twelve months from now - or from
        // when the current Premium ends, if it hasn't yet.
        [HttpPost("upgrade")]
        public IActionResult Upgrade([FromBody] UpgradeRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (!_options.DemoCheckout)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Demo checkout is switched off on this server.", code = "demo_checkout_off" });

            var option = PlanCatalogue.Find(request?.Billing);

            if (option is null)
                return BadRequest(new { message = "Billing must be Monthly, Quarterly or Annual.", code = "invalid" });

            return Ok(Describe(userId, _plans.Upgrade(userId, option)));
        }

        // Keeps Premium until it runs out, then you are on Free.
        [HttpPost("cancel")]
        public IActionResult Cancel()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var (outcome, status) = _plans.Cancel(userId);

            return outcome == CancelOutcome.NoActivePlan
                ? Conflict(new { message = "You don't have an active Premium plan to cancel.", code = "no_active_plan" })
                : Ok(Describe(userId, status));
        }

        private SubscriptionResponse Describe(int userId, PlanStatus status)
        {
            var usage = _usage.For(userId);

            return SubscriptionResponse.From(status, new UsageView(usage.ResumeVersions, usage.SavedJobs), _options.DemoCheckout);
        }
    }
}
