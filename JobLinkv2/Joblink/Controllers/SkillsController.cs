using Joblink.Services.MyData;
using JobLinkv2.Services.MyData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // The shared list of skill names. Reading it needs no login (it is a plain list of
    // words); a job seeker can add a skill; nobody renames or deletes one through the
    // API, because that would change every resume that uses it.
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : ControllerBase
    {
        private readonly SkillStore _skills;

        public SkillsController(SkillStore skills)
        {
            _skills = skills;
        }

        [HttpGet]
        public IActionResult GetAll() => Ok(_skills.List());

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            var skill = _skills.Get(id);

            return skill is null ? NotFound(new { message = "Skill not found." }) : Ok(skill);
        }

        // Adds a skill - or returns the one that already has this name (any capitals),
        // so the list never fills up with "SQL", "sql" and " Sql ".
        [HttpPost]
        [Authorize(Roles = "user")]
        public IActionResult Add([FromBody] CreateSkillRequest? request)
        {
            var name = request?.SkillName?.Trim();

            if (string.IsNullOrEmpty(name))
                return BadRequest(new { message = "A skill name is required.", code = "invalid" });

            return Ok(_skills.AddOrGet(name));
        }
    }
}
