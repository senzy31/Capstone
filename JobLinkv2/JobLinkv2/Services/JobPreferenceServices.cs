using JobLinkv2.Models;
using JobLinkv2.Repositories;

namespace JobLinkv2.Services
{
    public class JobPreferenceServices
    {
        private ClassRepositories<JobPreferenceModel> PreferenceRepository = new ClassRepositories<JobPreferenceModel>();

        public JobPreferenceModel? GetByUserId(int userId)
        {
            return PreferenceRepository.GetAll()
                .FirstOrDefault(preference => preference.UserId == userId);
        }

        public bool Add(JobPreferenceModel preference)
        {
            return PreferenceRepository.Add(preference);
        }

        public bool Update(JobPreferenceModel preference)
        {
            return PreferenceRepository.Update(preference);
        }
    }
}
