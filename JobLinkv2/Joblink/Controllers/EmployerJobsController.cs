using Joblink.Security;
using Joblink.Services.Employer;
using JobLinkv2.Services.Employer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Paid employer job posting. Payments are SIMULATED - purchasing grants credits, nothing
    // is charged. Job seekers get 403 on every route here; each job/credit read or write is
    // scoped to the caller's own employer id from the JWT, never a body/query value.
    [Route("api/employer")]
    [ApiController]
    [Authorize(Roles = "employer")]
    public class EmployerJobsController : ControllerBase
    {
        private readonly EmployerJobService _service;

        public EmployerJobsController(EmployerJobService service)
        {
            _service = service;
        }

        // Prices and credit grants, for the purchase modal.
        [HttpGet("posting-packages")]
        public IActionResult Packages() => Ok(_service.ListPackages());

        [HttpPost("purchase")]
        public IActionResult Purchase([FromBody] PurchaseRequest request)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var (outcome, view) = _service.Purchase(employerId, request.Package);

            return outcome == PurchaseOutcome.InvalidPackage
                ? BadRequest(new { message = "Package must be Single, Bundle5 or Renewal.", code = "invalid" })
                : Ok(view);
        }

        // This employer's own jobs: status, expiry, applicant count.
        [HttpGet("jobs")]
        public IActionResult List()
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            return Ok(_service.ListOwnJobs(employerId));
        }

        [HttpGet("jobs/{id:int}")]
        public IActionResult Get(int id)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var job = _service.DescribeOwnJob(employerId, id);

            return job is null ? NotFound() : Ok(job);
        }

        [HttpPost("jobs")]
        public IActionResult Create([FromBody] JobRequest request)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var (error, view) = _service.CreateDraft(employerId, request);

            return error != null ? BadRequest(new { message = error, code = "invalid" }) : Ok(view);
        }

        [HttpPut("jobs/{id:int}")]
        public IActionResult Update(int id, [FromBody] JobRequest request)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var (error, notFound, view) = _service.UpdateJob(employerId, id, request);

            if (error != null)
                return BadRequest(new { message = error, code = "invalid" });

            return notFound ? NotFound() : Ok(view);
        }

        // Spends one Post credit. No credit -> 403 with purchaseRequired: true, so the client
        // can send the employer straight to the purchase modal.
        [HttpPost("jobs/{id:int}/publish")]
        public async Task<IActionResult> Publish(int id, CancellationToken cancellationToken)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var (outcome, view) = await _service.PublishAsync(employerId, id, cancellationToken);

            return outcome switch
            {
                PublishOutcome.Ok => Ok(view),
                PublishOutcome.NotFound => NotFound(),
                PublishOutcome.NoCredit => NoCreditResponse("publish"),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        [HttpPost("jobs/{id:int}/close")]
        public IActionResult Close(int id)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            return _service.Close(employerId, id) ? Ok(_service.DescribeOwnJob(employerId, id)) : NotFound();
        }

        // Spends one Renewal credit and extends the job by 30 days.
        [HttpPost("jobs/{id:int}/renew")]
        public IActionResult Renew(int id)
        {
            if (User.GetUserId() is not int employerId)
                return Unauthorized();

            var outcome = _service.Renew(employerId, id);

            return outcome switch
            {
                RenewOutcome.Ok => Ok(_service.DescribeOwnJob(employerId, id)),
                RenewOutcome.NotFound => NotFound(),
                RenewOutcome.NoCredit => NoCreditResponse("renew"),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        private IActionResult NoCreditResponse(string action) => StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = $"You don't have a posting credit to {action} this job. Buy one first.",
            code = "no_credit",
            purchaseRequired = true
        });
    }
}
