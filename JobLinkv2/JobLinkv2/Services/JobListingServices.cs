using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
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

        public bool Add(JoblistingModel model)
        {
            return JoblistingRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return JoblistingRepository.Delete(id);
        }

        public bool Update(JoblistingModel model)
        {
            return JoblistingRepository.Update(model);
        }
    }
}
