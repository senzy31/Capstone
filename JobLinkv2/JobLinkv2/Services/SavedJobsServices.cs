using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class SavedJobsServices
    {
        private ClassRepositories<SavedJobsModel> SavedJobRepository = new ClassRepositories<SavedJobsModel>();

        public IEnumerable<SavedJobsModel> GetAll()
        {
            return SavedJobRepository.GetAll();
        }

        public SavedJobsModel GetById(int id)
        {
            return SavedJobRepository.GetById(id);
        }

        public bool Add(SavedJobsModel savedJobs)
        {
            return SavedJobRepository.Add(savedJobs);
        }

        public bool Delete(int id)
        {
            return SavedJobRepository.Delete(id);
        }

        public bool Update(SavedJobsModel savedJobs)
        {
            return SavedJobRepository.Update(savedJobs);
        }
    }
}
