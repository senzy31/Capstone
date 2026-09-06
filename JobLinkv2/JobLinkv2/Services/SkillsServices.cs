using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class SkillsServices
    {
        private ClassRepositories<SkillsModel> SkillsRepository = new ClassRepositories<SkillsModel>();

        public IEnumerable<SkillsModel> GetAll()
        {
            return SkillsRepository.GetAll();
        }

        public SkillsModel GetById(int id)
        {
            return SkillsRepository.GetById(id);
        }

        public bool Add(SkillsModel skills)
        {
            return SkillsRepository.Add(skills);
        }

        public bool Delete(int id)
        {
            return SkillsRepository.Delete(id);
        }

        public bool Update(SkillsModel skills)
        {
            return SkillsRepository.Update(skills);
        }
    }
}
