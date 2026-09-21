namespace JobLinkv2.Repositories
{
    public static class DbConfig
    {
        // Overridable with the ConnectionStrings:Joblink setting (e.g. the
        // ConnectionStrings__Joblink environment variable) in Program.cs.
        public const string DefaultConnectionString =
            "Server=(localdb)\\MSSQLLocalDB; Database=Joblinkv2; Trusted_Connection=true; MultipleActiveResultSets=true";
    }
}
