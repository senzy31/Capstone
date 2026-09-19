using JobLinkv2.Models;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobPreferenceController : ControllerBase
    {
        private static readonly string[] WorkArrangements = { "onsite", "remote", "hybrid" };
        private const decimal MaxSalaryValue = 100_000_000m;

        JobPreferenceServices preferenceServices = new JobPreferenceServices();
        UserServices userServices = new UserServices();

        // GET api/JobPreference/by-user/5  ->  404 until the user saves preferences
        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            var preference = preferenceServices.GetByUserId(userId);

            if (preference == null)
                return NotFound();

            return Ok(preference);
        }

        // PUT api/JobPreference/by-user/5 - creates the row on first save, updates it after.
        [HttpPut("by-user/{userId}")]
        public IActionResult Save(int userId, [FromBody] JobPreferenceModel? input)
        {
            if (input == null)
                return BadRequest(new { message = "Preferences are required." });

            if (userServices.GetUserId(userId) == null)
                return NotFound(new { message = "User not found." });

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

            var existing = preferenceServices.GetByUserId(userId);
            var record = existing ?? new JobPreferenceModel { UserId = userId };

            record.PreferredLocation = location;
            record.WorkArrangement = arrangement;
            record.MinSalary = input.MinSalary;
            record.MaxSalary = input.MaxSalary;
            record.IsDeleted = false;

            var saved = existing == null
                ? preferenceServices.Add(record)
                : preferenceServices.Update(record);

            if (!saved)
                return StatusCode(500, new { message = "Could not save your preferences." });

            return Ok(preferenceServices.GetByUserId(userId));
        }

        private static string? NullIfBlank(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
