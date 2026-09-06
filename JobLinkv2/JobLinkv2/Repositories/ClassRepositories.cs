using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobLinkv2.Repositories
{
    public class ClassRepositories<T> : GenericRepository<T> where T : class
    {
    }
}
