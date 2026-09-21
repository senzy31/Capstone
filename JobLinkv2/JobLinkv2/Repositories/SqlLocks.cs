namespace JobLinkv2.Repositories
{
    // "One request at a time" for a decision that reads and then writes (is there
    // already a profile? how many applications so far?), so two parallel requests
    // can't both see the old state and both act on it.
    //
    // This is one named application lock per kind and user - not range locks on
    // the table (UPDLOCK + HOLDLOCK). Range locks depend on the ORDER rows are
    // touched in, and parallel requests don't touch them in the same order, so SQL
    // Server ended up picking deadlock victims. A single named lock has no order to
    // get wrong, and other users are never held up.
    internal static class SqlLocks
    {
        // Put right after BEGIN TRANSACTION. Needs a @LockName parameter (see Name).
        // The lock is released when the transaction commits or rolls back.
        public const string Take = @"
                DECLARE @lock int;
                EXEC @lock = sp_getapplock @Resource = @LockName, @LockMode = 'Exclusive',
                                           @LockOwner = 'Transaction', @LockTimeout = 15000;

                IF @lock < 0
                BEGIN
                    ROLLBACK TRANSACTION;
                    THROW 51000, 'Could not get the lock for this user in time.', 1;
                END";

        public static string Name(string kind, int userId) => $"joblink:{kind}:{userId}";

        // For a decision about a value rather than a user (is there already a skill called this?).
        // Case-insensitive, like the columns it protects.
        public static string Name(string kind, string key) => $"joblink:{kind}:{key.ToLowerInvariant()}";
    }
}
