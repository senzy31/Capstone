using Microsoft.AspNetCore.Mvc;

namespace Joblink.Security
{
    // The one answer for "your plan doesn't allow that": a 403 that says why in words a person
    // can read, and whether upgrading would fix it. A Free user gets upgradeRequired = true (the
    // page shows an upgrade prompt); a Premium user who has hit a Premium cap gets false.
    public static class PlanResponses
    {
        public static ObjectResult LimitReached(ControllerBase controller, bool isPremium, string feature, int limit, string freeMessage, string premiumMessage) =>
            controller.StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = isPremium ? premiumMessage : freeMessage,
                code = isPremium ? "limit_reached" : "upgrade_required",
                upgradeRequired = !isPremium,
                feature,
                limit
            });
    }
}
