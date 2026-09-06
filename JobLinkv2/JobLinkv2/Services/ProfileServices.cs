using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class ProfileServices
    {
        private ClassRepositories<ProfileModel> ProfileRepository = new ClassRepositories<ProfileModel>();

        public IEnumerable<ProfileModel> GetAll()
        {
            return ProfileRepository.GetAll();
        }
        public ProfileModel GetById(int id)
        {
            return ProfileRepository.GetById(id);
        }

        public bool Add(ProfileModel model)
        {
            return ProfileRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return ProfileRepository.Delete(id);
        }

        public bool Update(ProfileModel model)
        {
            return ProfileRepository.Update(model);
        }
    }
}
