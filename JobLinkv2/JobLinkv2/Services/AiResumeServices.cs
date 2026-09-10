using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    /// <summary>
    /// Generates resume text with Claude. The API key lives server-side only
    /// (ANTHROPIC_API_KEY environment variable) - it never reaches the browser.
    /// </summary>
    public class AiResumeServices
    {
        private const string Model = "claude-opus-5";

        public async Task<string> GenerateSummaryAsync(AiSummaryRequest request)
        {
            var client = CreateClient();

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 1024,
                System =
                    "You write professional resume summaries. Write 2-4 sentences in third person " +
                    "without using the candidate's name, highlighting their strongest, most relevant " +
                    "points. No markdown, no quotation marks, no preamble like \"Here is...\" - " +
                    "respond with ONLY the summary text itself.",
                OutputConfig = new OutputConfig { Effort = Effort.Medium },
                Messages = [ new() { Role = Role.User, Content = BuildSummaryPrompt(request) } ]
            });

            return ExtractText(response);
        }

        public async Task<string> GenerateExperienceDescriptionAsync(AiExperienceRequest request)
        {
            var client = CreateClient();

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 1024,
                System =
                    "You write resume bullet points describing a job role's responsibilities and " +
                    "achievements. Return 2-4 bullet points, each on its own line starting with \"- \", " +
                    "each starting with a strong action verb (e.g. \"Led\", \"Built\", \"Reduced\"). " +
                    "No markdown bold/italics, no preamble - respond with ONLY the bullet points.",
                OutputConfig = new OutputConfig { Effort = Effort.Medium },
                Messages = [ new() { Role = Role.User, Content = BuildExperiencePrompt(request) } ]
            });

            return ExtractText(response);
        }

        private static AnthropicClient CreateClient()
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new AiNotConfiguredException(
                    "ANTHROPIC_API_KEY is not set. Set it as an environment variable on the machine " +
                    "running the API, then restart the API."
                );
            }

            return new AnthropicClient { ApiKey = apiKey };
        }

        private static string BuildSummaryPrompt(AiSummaryRequest request)
        {
            var skills = JoinOrFallback(request.Skills, "not specified");
            var experience = JoinOrFallback(request.ExperienceHighlights, "not specified");
            var education = JoinOrFallback(request.EducationHighlights, "not specified");

            var lines = new List<string>
            {
                "Draft a professional summary for a resume, using these details:",
                $"Target role / headline: {OrFallback(request.Headline)}",
                $"Skills: {skills}",
                $"Experience: {experience}",
                $"Education: {education}"
            };

            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                lines.Add($"Additional notes from the candidate: {request.Notes}");
            }

            return string.Join("\n", lines);
        }

        private static string BuildExperiencePrompt(AiExperienceRequest request)
        {
            var lines = new List<string>
            {
                "Draft resume bullet points for this role:",
                $"Position: {OrFallback(request.Position)}",
                $"Company: {OrFallback(request.CompanyName)}"
            };

            lines.Add(string.IsNullOrWhiteSpace(request.Notes)
                ? "The candidate didn't provide specifics - write plausible, credible responsibilities " +
                  "typical for this role and industry."
                : $"What the candidate actually did in this role: {request.Notes}");

            return string.Join("\n", lines);
        }

        private static string OrFallback(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "not specified" : value;

        private static string JoinOrFallback(List<string>? values, string fallback) =>
            values is { Count: > 0 } ? string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v))) : fallback;

        private static string ExtractText(Message response)
        {
            var text = response.Content
                .Select(block => block.Value)
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("The AI didn't return any text. Try again.");
            }

            return text.Trim();
        }
    }


    /// <summary>Thrown when ANTHROPIC_API_KEY isn't configured - distinct from a real API failure.</summary>
    public class AiNotConfiguredException : Exception
    {
        public AiNotConfiguredException(string message) : base(message) { }
    }


    public class AiSummaryRequest
    {
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public List<string>? Skills { get; set; }
        public List<string>? ExperienceHighlights { get; set; }
        public List<string>? EducationHighlights { get; set; }
        public string? Notes { get; set; }
    }


    public class AiExperienceRequest
    {
        public string? Position { get; set; }
        public string? CompanyName { get; set; }
        public string? Notes { get; set; }
    }
}
