using JobLinkv2.Repositories;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Joblink.Tests.Support
{
    // The same in-memory app as ApiFactory, but the data stores talk to the real Joblinkv2
    // database, and plan usage is counted from it. Only for the opt-in [DbFact] tests. (Plans
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
            });
        }

        // Puts a user on Premium for a month, on the test clock.
        public void MakePremium(int userId, int months = 1) =>
            Subscriptions.Set(new SubscriptionRecord(userId, "Premium", "Monthly", Clock.UtcNow, Clock.UtcNow.AddMonths(months), null));
    }
}
