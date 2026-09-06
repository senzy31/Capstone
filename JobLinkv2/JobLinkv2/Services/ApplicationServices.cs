using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class ApplicationServices
    {
        private ClassRepositories<ApplicationModel> ApplicationRepository = new ClassRepositories<ApplicationModel>();

        public IEnumerable<ApplicationModel> GetAll()
        {
            return ApplicationRepository.GetAll();
        }

        public ApplicationModel GetById(int id)
        {
            return ApplicationRepository.GetById(id);
        }

        public bool Add(ApplicationModel model)
        {
            return ApplicationRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return ApplicationRepository.Delete(id);
        }

        public bool Update(ApplicationModel model)
        {
            return ApplicationRepository.Update(model);
        }
    }
}
