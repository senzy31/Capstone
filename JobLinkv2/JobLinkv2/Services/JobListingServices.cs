using JobLinkv2.Models;
using JobLinkv2.Repositories;

namespace JobLinkv2.Services
{
    // Reads of the public job list. Nothing writes through here: a listing decides where
    // the Apply button sends people, so writes go through the import and the tracker's
    // own rules (see IApplyStore / ApplicationTrackerService).
    public class JobListingServices
    {
        private ClassRepositories<JoblistingModel> JoblistingRepository = new ClassRepositories<JoblistingModel>();

        public IEnumerable<JoblistingModel> GetAll()
        {
            return JoblistingRepository.GetAll();
        }

        public JoblistingModel GetById(int id)
        {
            return JoblistingRepository.GetById(id);
        }
    }
}
