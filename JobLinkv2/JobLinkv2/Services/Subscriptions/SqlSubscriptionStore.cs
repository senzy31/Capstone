using Dapper;
using JobLinkv2.Repositories;
using Microsoft.Data.SqlClient;

namespace JobLinkv2.Services.Subscriptions
{
    public sealed class SqlSubscriptionStore : ISubscriptionStore
    {
        // SQL Server error number: chosen as the victim of a deadlock.
        private const int DeadlockVictim = 1205;

        // "plan" is a reserved word in T-SQL, so it is always written [plan].
        private const string Columns = @"
            user_id AS UserId, [plan] AS [Plan], plan_billing AS Billing, premium_started_at AS StartedAt,
            premium_until AS Until, cancelled_at AS CancelledAt";

        private readonly string _connectionString;

        public SqlSubscriptionStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        public SubscriptionRecord? Get(int userId)
        {
            using var db = Open();

            return db.QueryFirstOrDefault<SubscriptionRecord>(
                $"SELECT {Columns} FROM Subscriptions WHERE user_id = @userId", new { userId });
        }

        public SubscriptionRecord? Change(int userId, Func<SubscriptionRecord?, SubscriptionRecord?> change)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return ChangeOnce(userId, change);
                }
                catch (SqlException ex) when (ex.Number == DeadlockVictim && attempt < 3)
                {
                    // Nothing was committed, so trying again is safe.
                }
            }
        }

        private SubscriptionRecord? ChangeOnce(int userId, Func<SubscriptionRecord?, SubscriptionRecord?> change)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();

            // One change per user at a time: read, decide and write can't interleave with
            // another request's, so two parallel upgrades both count.
            var lockResult = db.ExecuteScalar<int>(@"
                DECLARE @lock int;
                EXEC @lock = sp_getapplock @Resource = @name, @LockMode = 'Exclusive',
                                           @LockOwner = 'Transaction', @LockTimeout = 15000;
                SELECT @lock;",
                new { name = SqlLocks.Name("subscription", userId) }, transaction);

            if (lockResult < 0)
                throw new InvalidOperationException("Could not get the subscription lock for this user in time.");

            var current = db.QueryFirstOrDefault<SubscriptionRecord>(
                $"SELECT {Columns} FROM Subscriptions WHERE user_id = @userId", new { userId }, transaction);

            var next = change(current);

            if (next is null)
            {
                transaction.Commit();
                return current;
            }

            db.Execute(@"
                UPDATE Subscriptions
                   SET [plan] = @Plan, plan_billing = @Billing, premium_started_at = @StartedAt,
                       premium_until = @Until, cancelled_at = @CancelledAt, updated_at = GETUTCDATE()
                 WHERE user_id = @UserId;

                IF @@ROWCOUNT = 0
                    INSERT INTO Subscriptions (user_id, [plan], plan_billing, premium_started_at, premium_until, cancelled_at)
                    VALUES (@UserId, @Plan, @Billing, @StartedAt, @Until, @CancelledAt);",
                new
                {
                    next.UserId,
                    Plan = new DbString { Value = next.Plan, IsAnsi = true, Length = 10 },
                    Billing = new DbString { Value = next.Billing, IsAnsi = true, Length = 10 },
                    next.StartedAt,
                    next.Until,
                    next.CancelledAt
                }, transaction);

            transaction.Commit();

            return next;
        }
    }
}
