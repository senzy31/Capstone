using Joblink.Security;
using Joblink.Services.Resume;
using Joblink.Services.Resumes;
using JobLinkv2.Models;
using JobLinkv2.Services.Accounts;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;
using JobLinkv2.Services.Resumes.Export;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Joblink.Controllers
{
    // A job seeker's own resumes. Every action is scoped to the caller: someone
    // else's resume is a 404, and asking for another user's list is a 403.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class ResumeController : ControllerBase
    {
        private readonly ResumeDataStore _data;
        private readonly TimeProvider _time;
        private readonly IPlanReader _plans;
        private readonly IUserStore _users;
        private readonly SkillStore _skills;
        private readonly IResumeDocumentService _documents;

        public ResumeController(
            ResumeDataStore data, TimeProvider time, IPlanReader plans,
            IUserStore users, SkillStore skills, IResumeDocumentService documents)
        {
            _data = data;
            _time = time;
            _plans = plans;
            _users = users;
            _skills = skills;
            _documents = documents;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var resume = _data.GetResume(userId, id);

            return resume is null ? NotFound(new { message = "Resume not found." }) : Ok(resume);
        }

        [HttpGet("by-user/{userId}")]
        public IActionResult GetByUserId(int userId)
        {
            if (User.GetUserId() is not int callerId)
                return Unauthorized();

            if (userId != callerId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own resumes." });

            return Ok(_data.ListResumes(callerId));
        }

        // Downloads one of your resumes as a real PDF or DOCX, built by JobLink-AI (the Python
        // service) from what is actually saved for it - never from anything the request sends
        // beyond which template and format. The ATS-friendly template is Premium only, checked
        // here from the plan in the database, the same as every other plan limit.
        [HttpGet("{id}/export")]
        public async Task<IActionResult> Export(int id, [FromQuery] string? format, [FromQuery] string? template, CancellationToken cancellationToken)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            format = (format ?? "pdf").Trim().ToLowerInvariant();
            template = (template ?? "").Trim().ToLowerInvariant();

            if (format != "pdf" && format != "docx")
                return BadRequest(new { message = "format must be pdf or docx." });

            if (!ResumeExportMapper.IsKnownTemplate(template))
                return BadRequest(new { message = $"template must be one of: {string.Join(", ", ResumeExportMapper.TemplateIds)}." });

            var premium = _plans.IsPremium(userId);

            if (template == "ats" && !PlanLimits.For(premium).AdvancedTemplates)
                return PlanResponses.FeatureLocked(this, "advancedTemplates",
                    "The ATS-friendly template is part of Premium. Upgrade to Premium to use it.");

            var resume = _data.GetResume(userId, id);

            if (resume is null)
                return NotFound(new { message = "Resume not found." });

            var user = _users.FindById(userId);

            if (user is null)
                return Unauthorized();

            var profile = _data.GetProfileByUser(userId);
            var education = _data.ListEducation(userId, id) ?? Array.Empty<EducationModel>();
            var experience = _data.ListExperience(userId, id) ?? Array.Empty<ExperienceModel>();
            var skillLinks = _data.ListResumeSkills(userId, id) ?? Array.Empty<ResumeSkillsModel>();

            var skillNames = _skills.List().ToDictionary(skill => skill.SkillId, skill => skill.SkillName);

            var skills = skillLinks
                .Select(link => skillNames.TryGetValue(link.SkillId, out var name) ? name : null)
                .Where(name => name != null)
                .Cast<string>()
                .ToList();

            var dto = ResumeExportMapper.Build(user, profile, education, experience, skills, resume.AiGeneratedContent, template);

            var result = format == "pdf"
                ? await _documents.GeneratePdfAsync(dto, cancellationToken)
                : await _documents.GenerateDocxAsync(dto, cancellationToken);

            if (!result.Ok)
                return StatusCode(result.Status, new { message = result.Message });

            return File(result.Value!.Bytes, result.Value.ContentType, result.Value.FileName);
        }

        // Creates a resume for you (the owner is never taken from the request). Free plans keep
        // one saved resume, Premium up to ten: past that it is a 403, decided here on the server
        // from the plan in the database - hiding a button is not the limit.
        [HttpPost]
        public IActionResult Add([FromBody] CreateResumeRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var premium = _plans.IsPremium(userId);
            var limit = PlanLimits.For(premium).ResumeVersions;

            var created = _data.TryAddResume(
                userId,
                string.IsNullOrWhiteSpace(request?.Title) ? "My Resume" : request!.Title!.Trim(),
                string.IsNullOrWhiteSpace(request?.TemplateType) ? "standard" : request!.TemplateType!.Trim(),
                _time.GetLocalNow().DateTime,
                limit);

            return created is null
                ? PlanResponses.LimitReached(this, premium, "resumeVersions", limit,
                    freeMessage: $"Free accounts can save {limit} resume. Upgrade to Premium to save up to {PlanLimits.Premium.ResumeVersions}.",
                    premiumMessage: $"You've reached the limit of {limit} saved resumes. Delete one to make room.")
                : Ok(created);
        }

        // Saves the title and content of one of your resumes.
        [HttpPut]
        public IActionResult Update([FromBody] UpdateResumeRequest? request)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            if (request?.ResumeId is not int resumeId)
                return BadRequest(new { message = "A resume id is required.", code = "invalid" });

            var saved = _data.UpdateResume(
                userId,
                resumeId,
                string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                string.IsNullOrWhiteSpace(request.TemplateType) ? null : request.TemplateType.Trim(),
                request.AiGeneratedContent);

            return saved
                ? Ok(_data.GetResume(userId, resumeId))
                : NotFound(new { message = "Resume not found." });
        }

        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            return _data.DeleteResume(userId, id)
                ? Ok(new { message = "Resume deleted." })
                : NotFound(new { message = "Resume not found." });
        }
    }
}
