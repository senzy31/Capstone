using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class ResumeSkillsServices
    {
        private ClassRepositories<ResumeSkillsModel> ResumeSkillRepository = new ClassRepositories<ResumeSkillsModel>();

        public IEnumerable<ResumeSkillsModel> GetAll()
        {
            return ResumeSkillRepository.GetAll();
        }

        public ResumeSkillsModel GetById(int id)
        {
            return ResumeSkillRepository.GetById(id);
        }

        public bool Add(ResumeSkillsModel resumeSkills)
        {
            return ResumeSkillRepository.Add(resumeSkills);
        }

        public bool Delete(int id)
        {
            return ResumeSkillRepository.Delete(id);
        }

        public bool Update(ResumeSkillsModel resumeSkills)
        {
            return ResumeSkillRepository.Update(resumeSkills);
        }
    }
}
