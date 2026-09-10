using Anthropic.Exceptions;
using JobLinkv2.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Joblink.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AiResumeController : ControllerBase
    {
        AiResumeServices aiResumeServices = new AiResumeServices();

        [HttpPost("summary")]
        public async Task<IActionResult> GenerateSummary([FromBody] AiSummaryRequest request)
        {
            try
            {
                var summary = await aiResumeServices.GenerateSummaryAsync(request);

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
            try
            {
                var description = await aiResumeServices.GenerateExperienceDescriptionAsync(request);

                return Ok(new { description });
            }
            catch (Exception ex)
            {
                return HandleAiError(ex);
            }
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
