using JobLinkv2.Repositories;

namespace Joblink.Tests.Support
{
    // The same in-memory app as ApiFactory, but the resume / profile store talks to
    // the real Joblinkv2 database. Only for the opt-in [DbFact] tests.
    public sealed class DbApiFactory : ApiFactory
    {
        protected override string ResumeConnectionString => DbConfig.DefaultConnectionString;
    }
}
