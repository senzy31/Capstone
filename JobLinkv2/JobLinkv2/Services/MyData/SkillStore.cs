using Dapper;
using JobLinkv2.Models;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.MyData
{
    // The shared list of skill names everyone picks from. Anyone may read it; job
    // seekers may add to it; nobody changes or deletes a name through the API (a rename
    // or delete would change every resume that uses it).
    public sealed class SkillStore
    {
        private const string Columns = "skill_id AS SkillId, skill_name AS SkillName, ISNULL(is_deleted, 0) AS IsDeleted";

        private readonly string _connectionString;

        public SkillStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        public IReadOnlyList<SkillsModel> List()
        {
            using var db = Open();

            return db.Query<SkillsModel>($"SELECT {Columns} FROM Skills WHERE is_deleted = 0 ORDER BY skill_id").ToList();
        }

        public SkillsModel? Get(int skillId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<SkillsModel>(
                $"SELECT {Columns} FROM Skills WHERE skill_id = @skillId AND is_deleted = 0", new { skillId });
        }

        // The skill with this name - created if there isn't one. Names match without
        // regard to capitals ("SQL" and "sql" are one skill), and a skill that was removed
        // is brought back rather than duplicated. Two people adding the same new name at
        // once get the same skill.
        public SkillsModel AddOrGet(string name)
        {
            using var db = Open();

            var id = db.QuerySingle<int>($@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {SqlLocks.Take}

                DECLARE @id int = (SELECT TOP 1 skill_id FROM Skills WHERE skill_name = @name ORDER BY is_deleted, skill_id);

                IF @id IS NULL
                BEGIN
                    INSERT INTO Skills (skill_name, is_deleted) VALUES (@name, 0);
                    SET @id = CAST(SCOPE_IDENTITY() AS int);
                END
                ELSE
                    UPDATE Skills SET is_deleted = 0 WHERE skill_id = @id AND is_deleted = 1;

                COMMIT TRANSACTION;
                SELECT @id;",
                new { LockName = SqlLocks.Name("skill", name), name = new DbString { Value = name, IsAnsi = true, Length = 100 } });

            return db.QuerySingle<SkillsModel>($"SELECT {Columns} FROM Skills WHERE skill_id = @id", new { id });
        }
    }
}
