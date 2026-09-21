using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;

namespace JobLinkv2.Services.Subscriptions
{
    // How much of the plan's limits a user has used.
    public sealed record PlanUsage(int ResumeVersions, int SavedJobs);

    public interface IUsageReader
    {
        PlanUsage For(int userId);
    }

    public sealed class StoreUsageReader : IUsageReader
    {
        private readonly ResumeDataStore _resumes;
        private readonly UserDataStore _userData;

        public StoreUsageReader(ResumeDataStore resumes, UserDataStore userData)
        {
            _resumes = resumes;
            _userData = userData;
        }

        public PlanUsage For(int userId) =>
            new(_resumes.CountResumes(userId), _userData.CountSavedJobs(userId));
    }
}
