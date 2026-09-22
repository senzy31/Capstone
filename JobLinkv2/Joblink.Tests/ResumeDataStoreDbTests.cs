using Dapper;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Resumes;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The store on its own, called the way a careless caller might: as the wrong
    // user, with an id that isn't theirs. Every method has to refuse in SQL - the
    // controllers check first, but this layer must not depend on that.
    public sealed class ResumeDataStoreDbTests : IDisposable
    {
        private readonly ResumeDataStore _store = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser()
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', 'user', GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com" });

            _userIds.Add(id);

            return id;
        }

        private int NewSkill()
        {
            using var db = Open();

            return db.QuerySingle<int>(
                "INSERT INTO Skills (skill_name, is_deleted) VALUES (@name, 0); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest-{_tag}-skill" });
        }

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Resume_Skills WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Education     WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Experience    WHERE resume_id IN (SELECT resume_id FROM Resumes WHERE user_id IN @users);
                DELETE FROM Resumes WHERE user_id IN @users;
                DELETE FROM Profiles WHERE user_id IN @users;
                DELETE FROM Job_Preferences WHERE user_id IN @users;
                DELETE FROM Skills WHERE skill_name LIKE @skills;
                DELETE FROM Users WHERE user_id IN @users;",
                new { users, skills = $"dbtest-{_tag}-%" });
        }

        private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0);

        [DbFact]
        public void Profile_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var profile = _store.AddProfile(a, new ProfileFields("111", "Makati", null, null))!;

            Assert.Null(_store.GetProfile(b, profile.ProfileId));
            Assert.Null(_store.GetProfileByUser(b));
            Assert.False(_store.UpdateProfile(b, profile.ProfileId, new ProfileFields("HACKED", null, null, null)));
            Assert.False(_store.DeleteProfile(b, profile.ProfileId));

            var still = _store.GetProfile(a, profile.ProfileId)!;

            Assert.Equal(("111", "Makati", a), (still.Phone, still.Address, still.UserId));
        }

        [DbFact]
        public void A_user_gets_one_profile()
        {
            var a = NewUser();

            Assert.NotNull(_store.AddProfile(a, new ProfileFields("1", null, null, null)));
            Assert.Null(_store.AddProfile(a, new ProfileFields("2", null, null, null)));
            Assert.Equal("1", _store.GetProfileByUser(a)!.Phone);
        }

        [DbFact]
        public void Resume_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var resume = _store.AddResume(a, "Private", "standard", Now);

            Assert.Null(_store.GetResume(b, resume.ResumeId));
            Assert.Empty(_store.ListResumes(b));
            Assert.False(_store.UpdateResume(b, resume.ResumeId, "HACKED", null, "HACKED"));
            Assert.False(_store.DeleteResume(b, resume.ResumeId));

            var still = _store.GetResume(a, resume.ResumeId)!;

            Assert.Equal(("Private", a), (still.Title, still.UserId));
        }

        [DbFact]
        public void Education_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var resume = _store.AddResume(a, "CV", "standard", Now);
            var entry = _store.AddEducation(a, resume.ResumeId, new EducationFields("UP", "BS", null, null))!;

            Assert.Null(_store.ListEducation(b, resume.ResumeId));
            Assert.Null(_store.GetEducation(b, entry.EducationId));
            Assert.Null(_store.AddEducation(b, resume.ResumeId, new EducationFields("INTRUDER", null, null, null)));
            Assert.False(_store.UpdateEducation(b, entry.EducationId, new EducationFields("HACKED", null, null, null)));
            Assert.False(_store.DeleteEducation(b, entry.EducationId));

            var all = _store.ListEducation(a, resume.ResumeId)!;

            Assert.Equal(new[] { "UP" }, all.Select(e => e.SchoolName).ToArray());
        }

        [DbFact]
        public void Experience_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var resume = _store.AddResume(a, "CV", "standard", Now);
            var entry = _store.AddExperience(a, resume.ResumeId, new ExperienceFields("Acme", "Dev", "did things", null, null))!;

            Assert.Null(_store.ListExperience(b, resume.ResumeId));
            Assert.Null(_store.GetExperience(b, entry.ExperienceId));
            Assert.Null(_store.AddExperience(b, resume.ResumeId, new ExperienceFields("INTRUDER", null, null, null, null)));
            Assert.False(_store.UpdateExperience(b, entry.ExperienceId, new ExperienceFields("HACKED", null, null, null, null)));
            Assert.False(_store.DeleteExperience(b, entry.ExperienceId));

            var all = _store.ListExperience(a, resume.ResumeId)!;

            Assert.Equal(new[] { "Acme" }, all.Select(e => e.CompanyName).ToArray());
        }

        [DbFact]
        public void Resume_skill_methods_refuse_the_wrong_user()
        {
            var (a, b) = (NewUser(), NewUser());
            var resume = _store.AddResume(a, "CV", "standard", Now);
            var skill = NewSkill();

            Assert.Equal(AddSkillOutcome.Added, _store.AddResumeSkill(a, resume.ResumeId, skill));

            Assert.Null(_store.ListResumeSkills(b, resume.ResumeId));
            Assert.Equal(AddSkillOutcome.ResumeNotFound, _store.AddResumeSkill(b, resume.ResumeId, skill));
            Assert.False(_store.RemoveResumeSkill(b, resume.ResumeId, skill));
            Assert.Equal(AddSkillOutcome.SkillNotFound, _store.AddResumeSkill(a, resume.ResumeId, 2_000_000_000));
            Assert.Single(_store.ListResumeSkills(a, resume.ResumeId)!);
        }

        [DbFact]
        public void Preferences_are_per_user()
        {
            var (a, b) = (NewUser(), NewUser());

            // Values nobody else has, and both saves' answers checked: an answer read from
            // the wrong user's row must not look right.
            var savedA = _store.SavePreference(a, new PreferenceFields($"Taguig-{_tag}", "remote", 30111, 60111));

            Assert.Null(_store.GetPreference(b));

            var savedB = _store.SavePreference(b, new PreferenceFields($"Cebu-{_tag}", "onsite", null, null));

            Assert.Equal(($"Taguig-{_tag}", a), (savedA.PreferredLocation, savedA.UserId));
            Assert.Equal(($"Cebu-{_tag}", b), (savedB.PreferredLocation, savedB.UserId));
            Assert.Equal($"Taguig-{_tag}", _store.GetPreference(a)!.PreferredLocation);
            Assert.Equal($"Cebu-{_tag}", _store.GetPreference(b)!.PreferredLocation);
        }

        [DbFact]
        public void Deleted_rows_are_invisible_and_so_is_everything_under_a_deleted_resume()
        {
            var a = NewUser();
            var resume = _store.AddResume(a, "CV", "standard", Now);
            var education = _store.AddEducation(a, resume.ResumeId, new EducationFields("UP", null, null, null))!;
            var experience = _store.AddExperience(a, resume.ResumeId, new ExperienceFields("Acme", null, null, null, null))!;
            var skill = NewSkill();
            _store.AddResumeSkill(a, resume.ResumeId, skill);

            Assert.True(_store.DeleteEducation(a, education.EducationId));
            Assert.Null(_store.GetEducation(a, education.EducationId));
            Assert.False(_store.DeleteEducation(a, education.EducationId));

            Assert.True(_store.DeleteResume(a, resume.ResumeId));

            Assert.Null(_store.GetResume(a, resume.ResumeId));
            Assert.Null(_store.ListExperience(a, resume.ResumeId));
            Assert.Null(_store.GetExperience(a, experience.ExperienceId));
            Assert.Null(_store.ListResumeSkills(a, resume.ResumeId));
            Assert.Null(_store.AddEducation(a, resume.ResumeId, new EducationFields("LATE", null, null, null)));
            Assert.Equal(AddSkillOutcome.ResumeNotFound, _store.AddResumeSkill(a, resume.ResumeId, skill));
        }

        private int Count(string sql, object? args = null)
        {
            using var db = Open();
            return db.QuerySingle<int>(sql, args);
        }

        // One per-user lock decides "is there already one?", so racing requests can't both say no.
        [DbFact]
        public void Parallel_profile_creates_for_one_user_leave_exactly_one_profile()
        {
            var a = NewUser();
            ThreadPool.SetMinThreads(100, 100);   // really run them all at once

            var results = Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Task.Run(() => _store.AddProfile(a, new ProfileFields($"{i}", null, null, null))))).GetAwaiter().GetResult();

            Assert.Equal(1, results.Count(p => p is not null));
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Profiles WHERE user_id = @a AND is_deleted = 0", new { a }));
        }

        // Many users saving at once must neither block each other into failures nor deadlock,
        // and two saves by the same user at once must still leave one row.
        [DbFact]
        public void Parallel_preference_saves_all_succeed_and_leave_one_row_per_user()
        {
            var users = Enumerable.Range(0, 30).Select(_ => NewUser()).ToList();
            ThreadPool.SetMinThreads(200, 200);
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            Task.WhenAll(users.SelectMany(u => new[] { u, u }).Select((u, i) => Task.Run(() =>
            {
                try { _store.SavePreference(u, new PreferenceFields($"Loc-{_tag}-{i}", "remote", 1000 + i, 90000)); }
                catch (Exception ex) { failures.Add(ex.Message.Split('\n')[0]); }
            }))).GetAwaiter().GetResult();

            Assert.True(failures.IsEmpty, "saves failed: " + string.Join(" | ", failures.Distinct()));

            foreach (var u in users)
                Assert.Equal(1, Count("SELECT COUNT(*) FROM Job_Preferences WHERE user_id = @u AND is_deleted = 0", new { u }));
        }

        [DbFact]
        public void A_limited_add_stops_at_the_limit_counts_only_live_resumes_and_leaves_extras_alone()
        {
            var a = NewUser();

            // three already (more than the limit below): they stay, nothing is removed
            var made = Enumerable.Range(0, 3).Select(i => _store.AddResume(a, $"CV{i}", "standard", Now)).ToList();

            Assert.Null(_store.TryAddResume(a, "Nope", "standard", Now, maxLive: 2));
            Assert.Equal(3, _store.ListResumes(a).Count);

            // removed ones don't count: down to 1 live, two more fit under a limit of 3... one more up to the limit of 2
            _store.DeleteResume(a, made[0].ResumeId);
            _store.DeleteResume(a, made[1].ResumeId);

            Assert.NotNull(_store.TryAddResume(a, "Fits", "standard", Now, maxLive: 2));    // 1 live -> 2
            Assert.Null(_store.TryAddResume(a, "Nope", "standard", Now, maxLive: 2));       // at 2: full
            Assert.Equal(2, _store.ListResumes(a).Count);
        }

        [DbFact]
        public void A_limited_add_counts_each_users_own_resumes()
        {
            var (a, b) = (NewUser(), NewUser());

            Assert.NotNull(_store.TryAddResume(a, "A1", "standard", Now, maxLive: 1));
            Assert.Null(_store.TryAddResume(a, "A2", "standard", Now, maxLive: 1));
            Assert.NotNull(_store.TryAddResume(b, "B1", "standard", Now, maxLive: 1));       // A being full doesn't block B
        }

        [DbFact]
        public void Twenty_parallel_limited_adds_take_exactly_the_free_slots()
        {
            var a = NewUser();
            ThreadPool.SetMinThreads(100, 100);

            var results = Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Task.Run(() => _store.TryAddResume(a, $"CV{i}", "standard", Now, maxLive: 5)))).GetAwaiter().GetResult();

            Assert.Equal(5, results.Count(r => r is not null));
            Assert.Equal(5, _store.ListResumes(a).Count);
        }

        [DbFact]
        public void Counting_resumes_counts_only_that_users_live_ones()
        {
            var (a, b) = (NewUser(), NewUser());

            var gone = _store.AddResume(a, "Gone", "standard", Now);
            _store.AddResume(a, "One", "standard", Now);
            _store.AddResume(b, "B1", "standard", Now);
            _store.AddResume(b, "B2", "standard", Now);
            _store.AddResume(b, "B3", "standard", Now);
            _store.DeleteResume(a, gone.ResumeId);

            Assert.Equal(1, _store.CountResumes(a));
            Assert.Equal(3, _store.CountResumes(b));
        }

        [DbFact]
        public void An_entry_cannot_be_moved_to_another_resume_by_an_update()
        {
            var a = NewUser();
            var first = _store.AddResume(a, "One", "standard", Now);
            var second = _store.AddResume(a, "Two", "standard", Now);
            var entry = _store.AddEducation(a, first.ResumeId, new EducationFields("UP", null, null, null))!;

            Assert.True(_store.UpdateEducation(a, entry.EducationId, new EducationFields("UP Diliman", "BS", new DateTime(2020, 6, 15), null)));

            Assert.Equal(first.ResumeId, _store.GetEducation(a, entry.EducationId)!.ResumeId);
            Assert.Equal(new DateTime(2020, 6, 15), _store.GetEducation(a, entry.EducationId)!.StartDate);
            Assert.Empty(_store.ListEducation(a, second.ResumeId)!);
        }

        // ----- profile photo ----------------------------------------------------

        [DbFact]
        public void Setting_a_photo_with_no_profile_yet_creates_a_bare_one_and_the_key_resolves_it()
        {
            var user = NewUser();
            var bytes = new byte[] { 1, 2, 3 };

            var key = _store.SetPhoto(user, bytes, "image/jpeg");

            var row = _store.GetPhotoByKey(key);

            Assert.NotNull(row);
            Assert.Equal(bytes, row!.Photo);
            Assert.Equal("image/jpeg", row.ContentType);
        }

        [DbFact]
        public void Uploading_again_gets_a_new_key_and_the_old_one_stops_resolving()
        {
            var user = NewUser();

            var first = _store.SetPhoto(user, new byte[] { 1 }, "image/jpeg");
            var second = _store.SetPhoto(user, new byte[] { 2 }, "image/jpeg");

            Assert.NotEqual(first, second);
            Assert.Null(_store.GetPhotoByKey(first));
            Assert.Equal(new byte[] { 2 }, _store.GetPhotoByKey(second)!.Photo);
        }

        [DbFact]
        public void Removing_a_photo_clears_the_key_and_reports_whether_there_was_one()
        {
            var withPhoto = NewUser();
            var withoutPhoto = NewUser();
            var key = _store.SetPhoto(withPhoto, new byte[] { 1 }, "image/jpeg");

            Assert.True(_store.RemovePhoto(withPhoto));
            Assert.Null(_store.GetPhotoByKey(key));
            Assert.False(_store.RemovePhoto(withPhoto));   // nothing left to remove
            Assert.False(_store.RemovePhoto(withoutPhoto));
        }

        [DbFact]
        public void An_unknown_key_resolves_to_nothing()
        {
            NewUser();

            Assert.Null(_store.GetPhotoByKey(Guid.NewGuid()));
        }

        [DbFact]
        public void Setting_or_removing_one_users_photo_never_touches_another_users()
        {
            var a = NewUser();
            var b = NewUser();

            var aKey = _store.SetPhoto(a, new byte[] { 1 }, "image/jpeg");
            _store.SetPhoto(b, new byte[] { 2 }, "image/jpeg");

            _store.RemovePhoto(b);

            // b's removal didn't touch a's photo or key.
            Assert.NotNull(_store.GetPhotoByKey(aKey));
            Assert.Equal(new byte[] { 1 }, _store.GetPhotoByKey(aKey)!.Photo);
        }
    }
}
