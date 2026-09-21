using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The logged-in job seeker's own job preferences (used to score jobs).
    // The URL keeps the user id the pages already send, but it has to be yours.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class JobPreferenceController : ControllerBase
    {
        private static readonly string[] WorkArrangements = { "onsite", "remote", "hybrid" };
        private const decimal MaxSalaryValue = 100_000_000m;

        private readonly ResumeDataStore _data;

        public JobPreferenceController(ResumeDataStore data)
        {
            _data = data;
        }

        // GET api/JobPreference/by-user/5  ->  404 until the user saves preferences
        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own preferences." });

            var preference = _data.GetPreference(callerId);

            return preference is null ? NotFound() : Ok(preference);
        }

        // PUT api/JobPreference/by-user/5 - creates the row on first save, updates it after.
        [HttpPut("by-user/{userId}")]
        public IActionResult Save(int userId, [FromBody] SaveJobPreferenceRequest? input)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only change your own preferences." });

            if (input == null)
                return BadRequest(new { message = "Preferences are required." });

            var location = NullIfBlank(input.PreferredLocation);
            var arrangement = NullIfBlank(input.WorkArrangement)?.ToLowerInvariant();

            if (location != null && location.Length > 255)
                return BadRequest(new { message = "Preferred location is too long." });

            if (arrangement != null && !WorkArrangements.Contains(arrangement))
                return BadRequest(new { message = "Work arrangement must be onsite, remote or hybrid." });

            if (input.MinSalary is < 0 || input.MaxSalary is < 0)
                return BadRequest(new { message = "Salary cannot be negative." });

            if (input.MinSalary > MaxSalaryValue || input.MaxSalary > MaxSalaryValue)
                return BadRequest(new { message = "Salary is too large." });

            if (input.MinSalary != null && input.MaxSalary != null && input.MinSalary > input.MaxSalary)
                return BadRequest(new { message = "Minimum salary cannot be higher than maximum salary." });

            return Ok(_data.SavePreference(callerId, new PreferenceFields(location, arrangement, input.MinSalary, input.MaxSalary)));
        }

        private static string? NullIfBlank(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
