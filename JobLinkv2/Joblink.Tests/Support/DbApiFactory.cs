using JobLinkv2.Repositories;

namespace Joblink.Tests.Support
{
    // The same in-memory app as ApiFactory, but the data stores talk to
    // the real Joblinkv2 database. Only for the opt-in [DbFact] tests.
    public sealed class DbApiFactory : ApiFactory
    {
        protected override string DataConnectionString => DbConfig.DefaultConnectionString;
    }
}
