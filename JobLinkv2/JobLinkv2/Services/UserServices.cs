using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class UserServices
    {
        private ClassRepositories<UserModel> UserRepository = new ClassRepositories<UserModel>();

        public IEnumerable<UserModel> GetAll()
        {
            return UserRepository.GetAll();
        }

        public UserModel GetUserId(int id)
        {
            return UserRepository.GetById(id);
        }

        public bool AddUser(UserModel user)
        {
            return UserRepository.Add(user);
        }

        public bool DeleteUser(int id)
        {
            return UserRepository.Delete(id);
        }

        public bool UpdateUser(UserModel user)
        {
            return UserRepository.Update(user);
        }
    }
}
