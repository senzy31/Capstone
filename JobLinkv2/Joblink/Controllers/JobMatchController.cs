using Joblink.Security;
using JobLinkv2.Services.MyData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // A job seeker's own job matches, read only. Matches are produced by the server
    // (the matching service), so a client can't add, change or delete one - if it could,
    // it could give itself a perfect score.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class JobMatchController : ControllerBase
    {
        private readonly UserDataStore _data;

        public JobMatchController(UserDataStore data)
        {
            _data = data;
        }

        // Your matches, best first (this used to return everyone's).
        [HttpGet]
        public IActionResult GetAll()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return Ok(_data.ListMatches(userId));
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var match = _data.GetMatch(userId, id);

            return match is null ? NotFound(new { message = "Match not found." }) : Ok(match);
        }
    }
}
