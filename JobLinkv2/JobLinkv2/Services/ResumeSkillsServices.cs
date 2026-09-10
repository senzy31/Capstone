using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;
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

        // Resume_Skills has a composite primary key (resume_id, skill_id) and
        // no surrogate id, so it never fits GenericRepository's GetById/Update/
        // Delete(int) - those all assume a single [Key] property. GetAll/Add
        // don't need a key at all, so they're fine through the generic path;
        // the composite lookup/delete below talk to the table directly.
        private const string ConnectionString =
            "Server=(localdb)\\MSSQLLocalDB; Database=Joblinkv2; Trusted_Connection=true; MultipleActiveResultSets=true";

        public IEnumerable<ResumeSkillsModel> GetAll()
        {
            return ResumeSkillRepository.GetAll();
        }

        public ResumeSkillsModel GetById(int id)
        {
            return ResumeSkillRepository.GetById(id);
        }

        public IEnumerable<ResumeSkillsModel> GetByResumeId(int resumeId)
        {
            return ResumeSkillRepository.GetAll()
                .Where(link => link.ResumeId == resumeId);
        }

        public bool Add(ResumeSkillsModel resumeSkills)
        {
            return ResumeSkillRepository.Add(resumeSkills);
        }

        public bool Delete(int id)
        {
            return ResumeSkillRepository.Delete(id);
        }

        public bool Remove(int resumeId, int skillId)
        {
            using var connection = new SqlConnection(ConnectionString);

            const string query =
                "UPDATE [Resume_Skills] SET is_deleted = 1 WHERE resume_id = @resumeId AND skill_id = @skillId";

            int affectedRows = connection.Execute(query, new { resumeId, skillId });

            return affectedRows >= 1;
        }

        public bool Update(ResumeSkillsModel resumeSkills)
        {
            return ResumeSkillRepository.Update(resumeSkills);
        }
    }
}
