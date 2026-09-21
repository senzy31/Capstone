using Anthropic.Exceptions;
using Joblink.Security;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Joblink.Controllers
{
    // Resume text written by Claude. Every request costs money on the server's own
    // Anthropic key, so it needs a job seeker login and each user gets a fixed number
    // per hour - otherwise anyone who found the address could run up the bill.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "user")]
    public class AiResumeController : ControllerBase
    {
        public const int RequestsPerHour = 20;

        private readonly IAiResumeGenerator _ai;
        private readonly UserRateLimiter _limiter;

        public AiResumeController(IAiResumeGenerator ai, UserRateLimiter limiter)
        {
            _ai = ai;
            _limiter = limiter;
        }

        [HttpPost("summary")]
        public async Task<IActionResult> GenerateSummary([FromBody] AiSummaryRequest request)
        {
            if (TooMany() is { } limited)
                return limited;

            try
            {
                var summary = await _ai.GenerateSummaryAsync(request);

                return Ok(new { summary });
            }
            catch (Exception ex)
            {
                return HandleAiError(ex);
            }
        }

        [HttpPost("experience-description")]
        public async Task<IActionResult> GenerateExperienceDescription([FromBody] AiExperienceRequest request)
        {
            if (TooMany() is { } limited)
                return limited;

            try
            {
                var description = await _ai.GenerateExperienceDescriptionAsync(request);

                return Ok(new { description });
            }
            catch (Exception ex)
            {
                return HandleAiError(ex);
            }
        }

        // Both endpoints share one allowance per user. Requests that fail validation
        // never get here, so they don't use any of it.
        private IActionResult? TooMany()
        {
            if (User.GetUserId() is not int userId)
                return Unauthorized();

            var decision = _limiter.TryTake($"ai:{userId}", RequestsPerHour, TimeSpan.FromHours(1));

            if (decision.Allowed)
                return null;

            var seconds = (int)Math.Ceiling(decision.RetryAfter.TotalSeconds);
            var minutes = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalMinutes));

            Response.Headers["Retry-After"] = seconds.ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"You've used your {RequestsPerHour} AI requests for this hour. Try again in about {minutes} minute{(minutes == 1 ? "" : "s")}.",
                retryAfterSeconds = seconds
            });
        }

        // Most-specific exception first - each category gets a distinct,
        // actionable message instead of one generic "something went wrong".
        private IActionResult HandleAiError(Exception ex)
        {
            switch (ex)
            {
                case AiNotConfiguredException notConfigured:
                    return StatusCode(503, new { message = notConfigured.Message });

                case AnthropicUnauthorizedException:
                    return StatusCode(503, new { message = "ANTHROPIC_API_KEY was rejected. Check that it's valid and not expired." });

                case AnthropicRateLimitException:
                    return StatusCode(429, new { message = "The AI service is rate-limited right now. Try again in a moment." });

                case Anthropic5xxException:
                    return StatusCode(502, new { message = "The AI service is temporarily unavailable. Try again shortly." });

                case AnthropicIOException:
                    return StatusCode(502, new { message = "Couldn't reach the AI service. Check the API server's internet connection." });

                case AnthropicApiException apiEx:
                    return StatusCode(502, new { message = "The AI service returned an error.", detail = apiEx.Message });

                default:
                    return StatusCode(500, new { message = "Something went wrong generating that text.", detail = ex.Message });
            }
        }
    }
}
