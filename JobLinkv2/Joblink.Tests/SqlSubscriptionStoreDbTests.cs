using Dapper;
using Joblink.Tests.Support;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Subscriptions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Joblink.Tests
{
    // The real SqlSubscriptionStore against the real Joblinkv2 database: the SQL the
    // in-memory fake can't check - the table's constraints, the [plan] column, and that
    // parallel changes to one user's plan don't lose each other. Every test makes its own
    // users and deletes them afterwards.
    public sealed class SqlSubscriptionStoreDbTests : IDisposable
    {
        private readonly SqlSubscriptionStore _store = new(DbConfig.DefaultConnectionString);
        private readonly string _tag = Guid.NewGuid().ToString("N")[..10];
        private readonly List<int> _userIds = new();
        private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

        private SqlConnection Open()
        {
            var connection = new SqlConnection(DbConfig.DefaultConnectionString);
            connection.Open();
            return connection;
        }

        private int NewUser(string role = "user")
        {
            using var db = Open();

            var id = db.QuerySingle<int>(
                @"INSERT INTO Users (full_name, email, password_hash, role, created_at, is_deleted)
                  VALUES (@name, @email, 'x', @role, GETDATE(), 0);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name = $"dbtest {_tag}", email = $"dbtest.{_tag}.{_userIds.Count}@example.com", role });

            _userIds.Add(id);

            return id;
        }

        private static BillingOption Option(string billing) => PlanCatalogue.Find(billing)!;

        public void Dispose()
        {
            using var db = Open();

            var users = _userIds.Count > 0 ? _userIds : new List<int> { -1 };

            db.Execute(@"
                DELETE FROM Subscriptions WHERE user_id IN @users;
                DELETE FROM Users WHERE user_id IN @users;", new { users });
        }

        [DbFact]
        public void A_user_with_no_row_has_no_subscription()
        {
            Assert.Null(_store.Get(NewUser()));
        }

        [DbFact]
        public void A_change_creates_the_row_then_updates_it_and_everything_round_trips()
        {
            var user = NewUser();

            var first = _store.Change(user, current => SubscriptionRules.Upgrade(user, current, Option("Quarterly"), Now));

            Assert.Equal(new SubscriptionRecord(user, "Premium", "Quarterly", Now, Now.AddMonths(3), null), Normalize(first!));
            Assert.Equal(Normalize(first!), Normalize(_store.Get(user)!));

            var second = _store.Change(user, current => SubscriptionRules.Upgrade(user, current, Option("Annual"), Now));

            Assert.Equal(Now.AddMonths(15), Normalize(second!).Until);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Subscriptions WHERE user_id = @user", new { user }));

            var cancelled = _store.Change(user, current => SubscriptionRules.Cancel(current, Now, out _));

            Assert.Equal(Now, Normalize(cancelled!).CancelledAt);
        }

        [DbFact]
        public void A_change_that_answers_null_writes_nothing()
        {
            var user = NewUser();

            Assert.Null(_store.Change(user, _ => null));
            Assert.Null(_store.Get(user));

            _store.Change(user, current => SubscriptionRules.Upgrade(user, current, Option("Monthly"), Now));

            var before = Value<DateTime>("SELECT updated_at FROM Subscriptions WHERE user_id = @user", new { user });

            Thread.Sleep(50);

            var same = _store.Change(user, _ => null);

            Assert.NotNull(same);
            Assert.Equal(before, Value<DateTime>("SELECT updated_at FROM Subscriptions WHERE user_id = @user", new { user }));
        }

        // Parallel upgrades read, decide and write inside one lock per user, so none is lost.
        [DbFact]
        public void Twenty_parallel_upgrades_all_count()
        {
            var user = NewUser();
            ThreadPool.SetMinThreads(100, 100);

            Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
                _store.Change(user, current => SubscriptionRules.Upgrade(user, current, Option("Monthly"), Now))))).GetAwaiter().GetResult();

            Assert.Equal(Now.AddMonths(20), Normalize(_store.Get(user)!).Until);
            Assert.Equal(1, Count("SELECT COUNT(*) FROM Subscriptions WHERE user_id = @user", new { user }));
        }

        [DbFact]
        public void Different_users_upgrading_at_once_do_not_disturb_each_other()
        {
            var users = Enumerable.Range(0, 15).Select(_ => NewUser()).ToList();
            ThreadPool.SetMinThreads(100, 100);

            Task.WhenAll(users.Select(u => Task.Run(() =>
                _store.Change(u, current => SubscriptionRules.Upgrade(u, current, Option("Annual"), Now))))).GetAwaiter().GetResult();

            foreach (var u in users)
                Assert.Equal(Now.AddMonths(12), Normalize(_store.Get(u)!).Until);
        }

        [DbFact]
        public void A_change_that_throws_leaves_the_row_alone()
        {
            var user = NewUser();
            _store.Change(user, current => SubscriptionRules.Upgrade(user, current, Option("Monthly"), Now));

            Assert.Throws<InvalidOperationException>(() => _store.Change(user, _ => throw new InvalidOperationException("boom")));

            Assert.Equal(Now.AddMonths(1), Normalize(_store.Get(user)!).Until);
        }

        // ----- the table's own rules --------------------------------------------

        [DbFact]
        public void The_table_refuses_a_premium_row_without_billing_or_dates()
        {
            var user = NewUser();

            Assert.Throws<SqlException>(() => Insert(user, "Premium", null, null, null));
            Assert.Throws<SqlException>(() => Insert(user, "Premium", "Monthly", null, Now));
            Assert.Throws<SqlException>(() => Insert(user, "Premium", "Monthly", Now, null));
        }

        [DbFact]
        public void The_table_refuses_a_free_row_that_has_billing()
        {
            Assert.Throws<SqlException>(() => Insert(NewUser(), "Free", "Monthly", null, null));
        }

        [DbFact]
        public void The_table_refuses_an_unknown_plan_or_billing_period()
        {
            var user = NewUser();

            Assert.Throws<SqlException>(() => Insert(user, "Gold", "Monthly", Now, Now.AddMonths(1)));
            Assert.Throws<SqlException>(() => Insert(user, "Premium", "Weekly", Now, Now.AddMonths(1)));
        }

        [DbFact]
        public void The_table_refuses_a_subscription_for_a_user_who_does_not_exist()
        {
            Assert.Throws<SqlException>(() => Insert(2_000_000_000, "Premium", "Monthly", Now, Now.AddMonths(1)));
        }

        [DbFact]
        public void A_free_row_with_no_dates_is_allowed()
        {
            var user = NewUser();

            Insert(user, "Free", null, null, null);

            Assert.False(SubscriptionRules.StatusOf(_store.Get(user), Now).IsPremium);
        }

        [DbFact]
        public void The_real_store_drives_the_service_through_upgrade_expiry_and_cancel()
        {
            var user = NewUser();
            var clock = new TestClock("2026-09-22T10:00:00Z");
            var service = new SubscriptionService(_store, clock, new InMemoryNotificationSender());

            Assert.False(service.IsPremium(user));

            service.Upgrade(user, Option("Monthly"));

            Assert.True(service.IsPremium(user));
            Assert.Equal(CancelOutcome.Cancelled, service.Cancel(user).Outcome);
            Assert.True(service.IsPremium(user));                       // cancelled, still Premium until the end

            clock.Advance(TimeSpan.FromDays(31));

            Assert.False(service.IsPremium(user));                      // and Free after it, with no job running
            Assert.Equal("Premium", Value<string>("SELECT [plan] FROM Subscriptions WHERE user_id = @user", new { user }));   // the row itself is unchanged
        }

        // ----- helpers ----------------------------------------------------------

        // DATETIME keeps about 3 ms; the values here are whole seconds, so this only fixes the Kind.
        private static SubscriptionRecord Normalize(SubscriptionRecord r) => r with
        {
            StartedAt = r.StartedAt is { } s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null,
            Until = r.Until is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null,
            CancelledAt = r.CancelledAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null
        };

        private void Insert(int user, string plan, string? billing, DateTime? started, DateTime? until)
        {
            using var db = Open();

            db.Execute(
                "INSERT INTO Subscriptions (user_id, [plan], plan_billing, premium_started_at, premium_until) VALUES (@user, @plan, @billing, @started, @until)",
                new { user, plan, billing, started, until });
        }

        private T Value<T>(string sql, object? args = null)
        {
            using var db = Open();
            return db.QuerySingle<T>(sql, args);
        }

        private int Count(string sql, object? args = null) => Value<int>(sql, args);
    }
}
