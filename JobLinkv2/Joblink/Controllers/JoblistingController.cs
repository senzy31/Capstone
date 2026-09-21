using JobLinkv2.Models;
using JobLinkv2.Services;
using JobLinkv2.Services.Apply;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // Job listings are public to read. Writing is restricted because a listing
    // decides where the Apply button sends people: if anyone could edit
    // apply_url, they could point job seekers at a phishing page.
    //   - POST only lets a job seeker add a job to their own tracker by hand;
    //     the server sets the source and clears every apply field.
    //   - PUT / DELETE are closed. Imported jobs are managed by the JSearch
    //     import, and employer-posted jobs will get their own owner-checked
    //     endpoints with employer job posting.
    [Route("api/[controller]")]
    [ApiController]
    public class JoblistingController : ControllerBase
    {
        JobListingServices joblistingServices = new JobListingServices();

        private readonly ApplicationTrackerService _tracker;

        public JoblistingController(ApplicationTrackerService tracker)
        {
            _tracker = tracker;
        }

        [HttpGet]
        public ActionResult GetAll()
        {
            var joblist = joblistingServices.GetAll();
            return Ok(joblist);
        }

        [HttpGet("{id}")]
        public JoblistingModel GetById(int id)
        {
            return joblistingServices.GetById(id);
        }

        // Returns the created listing (with its new jobId).
        [HttpPost]
        [Authorize(Roles = "user")]
        public ActionResult Add([FromBody] JoblistingModel? joblist)
        {
            if (joblist is null)
                return BadRequest(new { message = "A job is required." });

            var result = _tracker.LogManualListing(joblist);

            return result.IsOk
                ? Ok(result.Value)
                : BadRequest(new { message = result.Message });
        }

        [HttpPut]
        [Authorize]
        public ActionResult Update()
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Job listings can't be edited through this endpoint." });
        }

        [HttpDelete]
        [Authorize]
        public ActionResult Delete()
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Job listings can't be deleted through this endpoint." });
        }
    }
}
