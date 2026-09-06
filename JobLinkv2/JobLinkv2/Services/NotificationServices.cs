using JobLinkv2.Models;
using JobLinkv2.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Services
{
    public class NotificationServices
    {
        private ClassRepositories<NotificationModel> NotificationRepository = new ClassRepositories<NotificationModel>();

        public IEnumerable<NotificationModel> GetAll()
        {
            return NotificationRepository.GetAll();
        }
        public NotificationModel GetById(int id)
        {
            return NotificationRepository.GetById(id);
        }

        public bool Add(NotificationModel model)
        {
            return NotificationRepository.Add(model);
        }

        public bool Delete(int id)
        {
            return NotificationRepository.Delete(id);
        }

        public bool Update(NotificationModel model)
        {
            return NotificationRepository.Update(model);
        }
    }
}
