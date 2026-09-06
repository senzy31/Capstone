using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class ResumeServices
    {
        private ClassRepositories<ResumeModel> ResumeRepository = new ClassRepositories<ResumeModel>();

        public IEnumerable<ResumeModel> GetAll()
        {
            return ResumeRepository.GetAll();
        }

        public ResumeModel GetById(int id)
        {
            return ResumeRepository.GetById(id);
        }

        public bool Add(ResumeModel resume)
        {
            return ResumeRepository.Add(resume);
        }

        public bool Delete(int id)
        {
            return ResumeRepository.Delete(id);
        }

        public bool Update(ResumeModel resume)
        {
            return ResumeRepository.Update(resume);
        }
    }
}
