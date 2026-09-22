using Joblink.Tests.Support;
using JobLinkv2.Services.Subscriptions;
using Xunit;

namespace Joblink.Tests
{
    // The plan rules as pure functions: who is Premium, what an upgrade or a cancel does.
    // No database, no HTTP, and time is just a value.
    public class SubscriptionRulesTests
    {
        private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

        private static SubscriptionRecord Premium(DateTime until, string billing = "Monthly", DateTime? started = null, DateTime? cancelled = null) =>
            new(1, "Premium", billing, started ?? until.AddMonths(-1), until, cancelled);

        private static BillingOption Option(string billing) => PlanCatalogue.Find(billing)!;

        // ----- who is Premium --------------------------------------------------------

        [Fact]
        public void A_user_with_no_row_is_on_the_free_plan()
        {
            var status = SubscriptionRules.StatusOf(null, Now);

            Assert.False(status.IsPremium);
            Assert.Equal("Free", status.Plan);
            Assert.Null(status.Billing);
        }

        [Fact]
        public void Premium_lasts_while_premium_until_is_ahead()
        {
            var status = SubscriptionRules.StatusOf(Premium(Now.AddSeconds(1)), Now);

            Assert.True(status.IsPremium);
            Assert.Equal("Premium", status.Plan);
            Assert.Equal("Monthly", status.Billing);
        }

        [Theory]
        [InlineData(0)]     // exactly at premium_until it is over: "until > now" is the rule
        [InlineData(-1)]
        [InlineData(-3600 * 24 * 90)]
        public void Premium_is_over_at_premium_until_and_after_without_anything_running(int secondsFromNow)
        {
            var record = Premium(Now.AddSeconds(secondsFromNow));

            var status = SubscriptionRules.StatusOf(record, Now);

            Assert.False(status.IsPremium);
            Assert.Equal("Free", status.Plan);
            Assert.Null(status.Billing);            // an expired billing period isn't a current one
            Assert.False(status.Cancelled);
            Assert.Equal(record.Until, status.Until);   // the date it ended is still known
        }

        [Fact]
        public void The_same_row_flips_from_premium_to_free_as_time_passes()
        {
            var record = Premium(Now.AddDays(10));

            Assert.True(SubscriptionRules.StatusOf(record, Now).IsPremium);
            Assert.True(SubscriptionRules.StatusOf(record, Now.AddDays(9)).IsPremium);
            Assert.False(SubscriptionRules.StatusOf(record, Now.AddDays(10)).IsPremium);
        }

        [Fact]
        public void A_row_that_says_free_is_free_whatever_its_dates_say()
        {
            var record = new SubscriptionRecord(1, "Free", null, Now, Now.AddYears(5), null);

            Assert.False(SubscriptionRules.StatusOf(record, Now).IsPremium);
        }

        // ----- upgrade ---------------------------------------------------------------

        [Theory]
        [InlineData("Monthly", 1)]
        [InlineData("Quarterly", 3)]
        [InlineData("Annual", 12)]
        public void Upgrading_a_free_user_gives_one_three_or_twelve_months_from_now(string billing, int months)
        {
            var record = SubscriptionRules.Upgrade(1, null, Option(billing), Now);

            Assert.Equal("Premium", record.Plan);
            Assert.Equal(billing, record.Billing);
            Assert.Equal(Now, record.StartedAt);
            Assert.Equal(Now.AddMonths(months), record.Until);
            Assert.Null(record.CancelledAt);
        }

        [Fact]
        public void Upgrading_while_premium_adds_after_the_current_end_and_keeps_the_start()
        {
            var started = Now.AddDays(-20);
            var current = Premium(Now.AddDays(10), "Monthly", started);

            var record = SubscriptionRules.Upgrade(1, current, Option("Quarterly"), Now);

            Assert.Equal(current.Until!.Value.AddMonths(3), record.Until);   // not now + 3 months: no time is wasted
            Assert.Equal(started, record.StartedAt);
            Assert.Equal("Quarterly", record.Billing);
        }

        [Fact]
        public void Upgrading_after_it_lapsed_starts_a_fresh_period_from_now()
        {
            var lapsed = Premium(Now.AddDays(-5), "Annual", Now.AddYears(-1).AddDays(-5));

            var record = SubscriptionRules.Upgrade(1, lapsed, Option("Monthly"), Now);

            Assert.Equal(Now, record.StartedAt);
            Assert.Equal(Now.AddMonths(1), record.Until);
            Assert.Equal("Monthly", record.Billing);
        }

        [Fact]
        public void Upgrading_takes_back_a_cancellation()
        {
            var cancelled = Premium(Now.AddDays(10), cancelled: Now.AddDays(-1));

            var record = SubscriptionRules.Upgrade(1, cancelled, Option("Monthly"), Now);

            Assert.Null(record.CancelledAt);
            Assert.False(SubscriptionRules.StatusOf(record, Now).Cancelled);
        }

        [Theory]
        [InlineData("2026-01-31", 1, "2026-02-28")]   // months are calendar months, clamped to the month's end
        [InlineData("2028-01-31", 1, "2028-02-29")]   // leap year
        [InlineData("2026-11-30", 3, "2027-02-28")]
        [InlineData("2026-02-28", 12, "2027-02-28")]
        public void Adding_months_follows_the_calendar(string start, int months, string end)
        {
            var from = DateTime.SpecifyKind(DateTime.Parse(start), DateTimeKind.Utc);
            var option = PlanCatalogue.Options.Single(o => o.Months == months);

            var record = SubscriptionRules.Upgrade(1, null, option, from);

            Assert.Equal(DateTime.Parse(end), record.Until!.Value.Date);
        }

        // ----- cancel ----------------------------------------------------------------

        [Fact]
        public void Cancelling_keeps_premium_until_the_end_date()
        {
            var current = Premium(Now.AddDays(10));

            var next = SubscriptionRules.Cancel(current, Now, out var outcome);

            Assert.Equal(CancelOutcome.Cancelled, outcome);
            Assert.Equal(Now, next!.CancelledAt);
            Assert.Equal(current.Until, next.Until);

            var status = SubscriptionRules.StatusOf(next, Now);

            Assert.True(status.IsPremium);
            Assert.True(status.Cancelled);
        }

        [Fact]
        public void A_cancelled_plan_becomes_free_when_it_runs_out()
        {
            var next = SubscriptionRules.Cancel(Premium(Now.AddDays(10)), Now, out _);

            Assert.False(SubscriptionRules.StatusOf(next, Now.AddDays(10)).IsPremium);
        }

        [Fact]
        public void Cancelling_twice_changes_nothing_the_second_time()
        {
            var cancelled = Premium(Now.AddDays(10), cancelled: Now.AddDays(-1));

            var next = SubscriptionRules.Cancel(cancelled, Now, out var outcome);

            Assert.Equal(CancelOutcome.AlreadyCancelled, outcome);
            Assert.Null(next);
        }

        [Fact]
        public void There_is_nothing_to_cancel_on_the_free_plan_or_after_it_lapsed()
        {
            SubscriptionRules.Cancel(null, Now, out var onFree);
            SubscriptionRules.Cancel(Premium(Now.AddDays(-1)), Now, out var onLapsed);

            Assert.Equal(CancelOutcome.NoActivePlan, onFree);
            Assert.Equal(CancelOutcome.NoActivePlan, onLapsed);
        }

        // ----- what each plan allows ------------------------------------------------

        [Fact]
        public void The_plans_allow_what_the_spec_says()
        {
            Assert.Equal((1, 10, true, false, false, false, false),
                (PlanLimits.Free.ResumeVersions, PlanLimits.Free.SavedJobs!.Value, PlanLimits.Free.ShowAds,
                 PlanLimits.Free.DetailedScore, PlanLimits.Free.MissingSkills, PlanLimits.Free.AdvancedTemplates, PlanLimits.Free.PriorityApplication));

            Assert.Equal((10, (int?)null, false, true, true, true, true),
                (PlanLimits.Premium.ResumeVersions, PlanLimits.Premium.SavedJobs, PlanLimits.Premium.ShowAds,
                 PlanLimits.Premium.DetailedScore, PlanLimits.Premium.MissingSkills, PlanLimits.Premium.AdvancedTemplates, PlanLimits.Premium.PriorityApplication));

            Assert.Same(PlanLimits.Free, PlanLimits.For(false));
            Assert.Same(PlanLimits.Premium, PlanLimits.For(true));
        }

        [Fact]
        public void The_prices_are_99_249_and_899_pesos_for_1_3_and_12_months()
        {
            Assert.Equal(new[] { ("Monthly", 1, 99), ("Quarterly", 3, 249), ("Annual", 12, 899) },
                PlanCatalogue.Options.Select(o => (o.Billing, o.Months, o.PricePhp)).ToArray());
        }

        [Theory]
        [InlineData("Monthly", "Monthly")]
        [InlineData("monthly", "Monthly")]
        [InlineData("  ANNUAL ", "Annual")]
        [InlineData("quarterly", "Quarterly")]
        public void A_billing_period_is_found_whatever_the_capitals(string given, string expected)
        {
            Assert.Equal(expected, PlanCatalogue.Find(given)!.Billing);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Weekly")]
        [InlineData("Lifetime")]
        [InlineData("1")]
        public void Anything_else_is_not_a_billing_period(string? given)
        {
            Assert.Null(PlanCatalogue.Find(given));
        }
    }

    public class SubscriptionServiceTests
    {
        private readonly InMemorySubscriptionStore _store = new();
        private readonly TestClock _clock = new();
        private readonly InMemoryNotificationSender _notifications;
        private readonly SubscriptionService _service;

        public SubscriptionServiceTests()
        {
            _notifications = new InMemoryNotificationSender(_clock);
            _service = new SubscriptionService(_store, _clock, _notifications);
        }

        private static BillingOption Option(string billing) => PlanCatalogue.Find(billing)!;

        [Fact]
        public void The_plan_is_read_fresh_each_time_so_an_upgrade_and_an_expiry_show_at_once()
        {
            Assert.False(_service.IsPremium(1));

            _service.Upgrade(1, Option("Monthly"));

            Assert.True(_service.IsPremium(1));

            _clock.Advance(TimeSpan.FromDays(31));

            Assert.False(_service.IsPremium(1));
        }

        [Fact]
        public void Upgrade_upgrade_cancel_expire_end_to_end()
        {
            var first = _service.Upgrade(1, Option("Monthly"));
            var second = _service.Upgrade(1, Option("Quarterly"));

            Assert.Equal(_clock.UtcNow.AddMonths(1), first.Until);
            Assert.Equal(_clock.UtcNow.AddMonths(4), second.Until);         // 1 + 3 months, stacked

            var (outcome, cancelled) = _service.Cancel(1);

            Assert.Equal(CancelOutcome.Cancelled, outcome);
            Assert.True(cancelled.IsPremium);
            Assert.True(cancelled.Cancelled);

            _clock.Advance(TimeSpan.FromDays(200));

            Assert.False(_service.GetStatus(1).IsPremium);
            Assert.Equal(CancelOutcome.NoActivePlan, _service.Cancel(1).Outcome);
        }

        [Fact]
        public void Users_are_independent()
        {
            _service.Upgrade(1, Option("Annual"));

            Assert.True(_service.IsPremium(1));
            Assert.False(_service.IsPremium(2));
        }

        [Fact]
        public void Parallel_upgrades_all_count()
        {
            Parallel.For(0, 24, _ => _service.Upgrade(1, Option("Monthly")));

            Assert.Equal(_clock.UtcNow.AddMonths(24), _service.GetStatus(1).Until);   // no lost update
        }

        [Fact]
        public void Upgrading_sends_a_PremiumActivated_notification()
        {
            _service.Upgrade(1, Option("Monthly"));

            var sent = Assert.Single(_notifications.Sent);
            Assert.Equal(1, sent.UserId);
            Assert.Equal("PremiumActivated", sent.Type);
            Assert.Contains("Monthly", sent.Message);
        }

        [Fact]
        public void CheckExpiryNotifications_does_nothing_for_someone_with_no_row_or_far_from_expiring()
        {
            _service.CheckExpiryNotifications(1);       // no row at all
            Assert.Empty(_notifications.Sent);

            _service.Upgrade(2, Option("Annual"));      // just the PremiumActivated from Upgrade
            _service.CheckExpiryNotifications(2);       // a year out - nowhere near the 3-day window

            Assert.DoesNotContain(_notifications.Sent, n => n.Type != "PremiumActivated");
        }

        [Fact]
        public void CheckExpiryNotifications_warns_within_3_days_of_expiring_but_only_once()
        {
            _service.Upgrade(1, Option("Monthly"));
            _clock.Advance(TimeSpan.FromDays(29));       // 2 days left on a 1-month plan

            _service.CheckExpiryNotifications(1);
            _service.CheckExpiryNotifications(1);        // asking again (another page load) doesn't repeat it

            var warning = Assert.Single(_notifications.Sent, n => n.Type == "PremiumExpiringSoon");
            Assert.Equal(1, warning.UserId);
            Assert.Contains("/DASHBOARD/Plans.html", warning.Link);
        }

        [Fact]
        public void CheckExpiryNotifications_reports_expired_once_and_never_for_a_plan_that_was_always_free()
        {
            _service.Upgrade(1, Option("Monthly"));
            _clock.Advance(TimeSpan.FromDays(31));        // just lapsed

            _service.CheckExpiryNotifications(1);
            _service.CheckExpiryNotifications(1);         // repeat check: still only once

            var expired = _notifications.Sent.Where(n => n.Type == "PremiumExpired").ToList();
            Assert.Single(expired);
            Assert.Equal(1, expired[0].UserId);

            // A user who was never Premium at all never gets an "expired" notification.
            _service.CheckExpiryNotifications(2);
            Assert.DoesNotContain(_notifications.Sent, n => n.UserId == 2);
        }

        [Fact]
        public void CheckExpiryNotifications_re_arms_for_a_second_premium_cycle()
        {
            _service.Upgrade(1, Option("Monthly"));
            _clock.Advance(TimeSpan.FromDays(31));
            _service.CheckExpiryNotifications(1);          // first "expired" notification

            _service.Upgrade(1, Option("Monthly"));         // buys Premium again
            _clock.Advance(TimeSpan.FromDays(31));
            _service.CheckExpiryNotifications(1);           // should fire again for THIS cycle's expiry

            Assert.Equal(2, _notifications.Sent.Count(n => n.Type == "PremiumExpired"));
        }
    }
}
