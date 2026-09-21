using Joblink.Security;
using Joblink.Services.Resumes;
using JobLinkv2.Services.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The logged-in user's own profile (phone, address, links). Employers have
    // one too, so any role may use it - but only for themselves. There is no
    // "list every profile".
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly ResumeDataStore _data;

        public ProfileController(ResumeDataStore data)
        {
            _data = data;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var profile = _data.GetProfile(userId, id);

            return profile is null ? NotFound(new { message = "Profile not found." }) : Ok(profile);
        }

        // 404 until the user saves a profile; 403 for anyone else's.
        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own profile." });

            var profile = _data.GetProfileByUser(callerId);

            return profile is null ? NotFound(new { message = "You haven't saved a profile yet." }) : Ok(profile);
        }

        // Creates your profile. There is one per user, so a second is a 409.
        [HttpPost]
        public IActionResult Add([FromBody] SaveProfileRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "Profile details are required.", code = "invalid" });

            var created = _data.AddProfile(userId, request.ToFields());

            return created is null
                ? Conflict(new { message = "You already have a profile - update it instead.", code = "profile_exists" })
                : Ok(created);
        }

        // Replaces the details of your own profile.
        [HttpPut]
        public IActionResult Update([FromBody] SaveProfileRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request is null)
                return BadRequest(new { message = "Profile details are required.", code = "invalid" });

            var current = _data.GetProfileByUser(userId);

            // An id that isn't yours is treated like one that doesn't exist.
            if (current is null || (request.ProfileId is int asked && asked != current.ProfileId))
                return NotFound(new { message = "Profile not found." });

            return _data.UpdateProfile(userId, current.ProfileId, request.ToFields())
                ? Ok(_data.GetProfile(userId, current.ProfileId))
                : NotFound(new { message = "Profile not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteProfile(userId, id)
                ? Ok(new { message = "Profile deleted." })
                : NotFound(new { message = "Profile not found." });
        }
    }
}
