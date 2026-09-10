using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class EducationServices
    {
        private ClassRepositories<EducationModel> EducationRepository = new ClassRepositories<EducationModel>();

        public IEnumerable<EducationModel> GetAll()
        {
            return EducationRepository.GetAll();
        }

        public EducationModel GetById(int id)
        {
            return EducationRepository.GetById(id);
        }

        public IEnumerable<EducationModel> GetByResumeId(int resumeId)
        {
            return EducationRepository.GetAll()
                .Where(edu => edu.ResumeId == resumeId);
        }

        public bool Add(EducationModel model)
        {
            return EducationRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return EducationRepository.Delete(id);
        }

        public bool Update(EducationModel model)
        {
            return EducationRepository.Update(model);
        }
    }
}
