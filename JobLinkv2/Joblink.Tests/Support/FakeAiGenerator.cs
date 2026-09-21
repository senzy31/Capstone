using JobLinkv2.Services;

namespace Joblink.Tests.Support
{
    // Stands in for Claude, so no test ever spends money or needs a key. Counts how
    // often it was asked, which is how the tests show a refused request cost nothing.
    public sealed class FakeAiGenerator : IAiResumeGenerator
    {
        private int _calls;

        public int Calls => _calls;

        // Set to make the next calls fail the way the real service can.
        public Exception? Fail { get; set; }

        public Task<string> GenerateSummaryAsync(AiSummaryRequest request)
        {
            Interlocked.Increment(ref _calls);

            if (Fail is not null)
                throw Fail;

            return Task.FromResult($"A summary for {request.FullName}.");
        }

        public Task<string> GenerateExperienceDescriptionAsync(AiExperienceRequest request)
        {
            Interlocked.Increment(ref _calls);

            if (Fail is not null)
                throw Fail;

            return Task.FromResult($"- Worked as {request.Position}.");
        }
    }
}
