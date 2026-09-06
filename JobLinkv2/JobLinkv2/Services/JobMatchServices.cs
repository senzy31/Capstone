using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class JobMatchServices
    {
        private ClassRepositories<JobMatchModel> JobMatchRepository = new ClassRepositories<JobMatchModel>();

        public IEnumerable<JobMatchModel> GetAll()
        {
            return JobMatchRepository.GetAll();
        }
        public JobMatchModel GetById(int id)
        {
            return JobMatchRepository.GetById(id);
        }

        public bool Add(JobMatchModel model)
        {
            return JobMatchRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return JobMatchRepository.Delete(id);
        }

        public bool Update(JobMatchModel model)
        {
            return JobMatchRepository.Update(model);
        }
    }
}
