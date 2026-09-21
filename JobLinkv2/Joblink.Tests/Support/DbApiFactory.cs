using JobLinkv2.Repositories;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Joblink.Tests.Support
{
    // The same in-memory app as ApiFactory, but the data stores talk to the real Joblinkv2
    // database, plan usage is counted from it, and recommendations read the real resume. Only for the opt-in [DbFact] tests. (Plans
    // themselves still come from the in-memory subscription store, on the test clock: these
    // tests are about what the limits do to real rows.)
    public sealed class DbApiFactory : ApiFactory
    {
        protected override string DataConnectionString => DbConfig.DefaultConnectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUsageReader>();
                services.AddSingleton<IUsageReader, StoreUsageReader>();

                // Recommendations read the caller's resume and preferences from the database, as they do for real.
                services.RemoveAll<IScoringProfileReader>();
                services.AddSingleton<IScoringProfileReader>(new SqlScoringProfileReader(
                    new ResumeDataStore(DbConfig.DefaultConnectionString), new SkillStore(DbConfig.DefaultConnectionString)));
            });
        }

        // Puts a user on Premium for a month, on the test clock.
        public void MakePremium(int userId, int months = 1) =>
            Subscriptions.Set(new SubscriptionRecord(userId, "Premium", "Monthly", Clock.UtcNow, Clock.UtcNow.AddMonths(months), null));
    }
}
