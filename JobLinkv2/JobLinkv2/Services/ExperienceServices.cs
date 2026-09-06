using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class ExperienceServices
    {
        private ClassRepositories<ExperienceModel> ExperienceRepository = new ClassRepositories<ExperienceModel>();

        public IEnumerable<ExperienceModel> GetAll()
        {
            return ExperienceRepository.GetAll();
        }
        public ExperienceModel GetById(int id)
        {
            return ExperienceRepository.GetById(id);
        }

        public bool Add(ExperienceModel model)
        {
            return ExperienceRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return ExperienceRepository.Delete(id);
        }

        public bool Update(ExperienceModel model)
        {
            return ExperienceRepository.Update(model);
        }
    }
}
