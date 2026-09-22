using Dapper;
using Joblink.Security;
using Joblink.Services;
using Joblink.Services.Accounts;
using Joblink.Services.Employer;
using Joblink.Services.JobSearch;
using Joblink.Services.Recommendations;
using Joblink.Services.Subscriptions;
using JobLinkv2.Repositories;
using JobLinkv2.Services;
using JobLinkv2.Services.Accounts;
using JobLinkv2.Services.Apply;
using JobLinkv2.Services.Employer;
using JobLinkv2.Services.Matching;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Notifications;
using JobLinkv2.Services.Resumes;
using JobLinkv2.Services.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // A request that fails validation answers { message, code } like every other
        // error, with the first thing that is wrong (instead of the default problem
        // document, which the pages would show as a blob).
        options.InvalidModelStateResponseFactory = context =>
        {
            var first = context.ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error =>
                {
                    // A rule's own message, or - for a value that couldn't be read at all,
                    // like a date that isn't one - the reader's message without its
                    // "Path: $.field | LineNumber..." tail.
                    var text = !string.IsNullOrWhiteSpace(error.ErrorMessage) ? error.ErrorMessage : error.Exception?.Message;

                    if (string.IsNullOrWhiteSpace(text))
                        return "That request isn't valid.";

                    var cut = text.IndexOf(" Path:", StringComparison.Ordinal);

                    return cut > 0 ? text[..cut] : text;
                })
                .FirstOrDefault() ?? "That request isn't valid.";

            return new BadRequestObjectResult(new { message = first, code = "invalid" });
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Used by JobSearchController to call JSearch (RapidAPI) server-side.
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// ✅ Login tokens (JWT). The signing key is a secret - see JwtOptions.
var jwtOptions = JwtOptions.From(builder.Configuration, builder.Environment);

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = jwtOptions.ToValidationParameters());

builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("Joblink") ?? DbConfig.DefaultConnectionString;

// ✅ Accounts: sign up, log in, your own details
builder.Services.AddSingleton<IUserStore>(new SqlUserStore(connectionString));
builder.Services.AddSingleton<UserAccountService>();

// ✅ A job seeker's own data: profile, resumes and what hangs off them, preferences
builder.Services.AddSingleton(new ResumeDataStore(connectionString));

// ✅ Profile photos: job seekers and employers alike. Validated by content, resized and
// stripped of metadata (ProfilePhotoProcessor), served from an unguessable, regenerated-on-
// every-upload key - never from a userId, so there is no "photo for user X" lookup anywhere.
builder.Services.AddSingleton<Joblink.Services.Profile.ProfilePhotoProcessor>();

// ✅ Notifications, saved jobs and matches (yours only), and the shared skills list
builder.Services.AddSingleton(new UserDataStore(connectionString));
builder.Services.AddSingleton(new SkillStore(connectionString));

// ✅ AI resume text: a job seeker login, and 20 requests an hour each
builder.Services.AddSingleton<IAiResumeGenerator, AiResumeServices>();
builder.Services.AddSingleton<UserRateLimiter>();

// ✅ Job seeker plans (Free / Premium). Payments are SIMULATED. The plan is read from the
// database on every request, never from the login token.
builder.Services.AddSingleton<ISubscriptionStore>(new SqlSubscriptionStore(connectionString));
builder.Services.AddSingleton<SubscriptionService>();
builder.Services.AddSingleton<IUsageReader, StoreUsageReader>();
builder.Services.AddSingleton<IPlanReader>(services => services.GetRequiredService<SubscriptionService>());
builder.Services.AddSingleton(new SubscriptionOptions { DemoCheckout = builder.Configuration.GetValue("Subscription:DemoCheckout", true) });

// ✅ Apply flow
builder.Services.AddSingleton<IApplyStore>(new SqlApplyStore(connectionString));
builder.Services.AddSingleton<ApplyService>();
builder.Services.AddSingleton<ApplicationTrackerService>();
builder.Services.AddSingleton<JobImportService>();

// ✅ Notifications: one sender, used by anything that needs to tell a user something
// (the apply flow's employer notice, and everything under Employer/ below).
builder.Services.AddSingleton<INotificationSender>(new SqlNotificationSender(connectionString));

// ✅ Paid employer job posting. Payments are SIMULATED - purchasing grants credits and charges
// nothing. Nominatim (OpenStreetMap, no key) geocodes a job's location on publish, best-effort -
// "Nominatim:BaseUrl" points it at a fake for tests/live checks, same override pattern as JSearch.
builder.Services.AddSingleton(new EmployerJobStore(connectionString));
builder.Services.AddSingleton<IGeocodingService, NominatimGeocodingService>();
builder.Services.AddSingleton<EmployerJobService>();

// ✅ The job feed (JSearch via RapidAPI, cached) and the recommendations built on it: each job scored
// against the caller's own resume, and shown as much of that score as their plan allows.
builder.Services.AddSingleton<IJobSearchService, RapidApiJobSearchService>();
builder.Services.AddSingleton<IScoringProfileReader>(services =>
    new SqlScoringProfileReader(services.GetRequiredService<ResumeDataStore>(), services.GetRequiredService<SkillStore>()));
builder.Services.AddSingleton<RecommendationService>();

// ✅ Resume documents (PDF/DOCX), built by JobLink-AI (Python, local service - "JobLinkAi:BaseUrl",
// default http://127.0.0.1:8001, no key). The ATS-friendly template is Premium only.
builder.Services.AddSingleton<Joblink.Services.Resume.IResumeDocumentService, Joblink.Services.Resume.PythonResumeService>();

// ✅ Add CORS here
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy
                .AllowAnyOrigin()   // or specify your frontend URL
                .AllowAnyHeader()
                .AllowAnyMethod()
                // Content-Disposition isn't one of the response headers a browser exposes to
                // fetch() by default cross-origin - without this, the resume download endpoint's
                // filename (GET /api/Resume/{id}/export) is invisible to the page that asked for it.
                .WithExposedHeaders("Content-Disposition");
        });
});

// ✅ Dapper config
DefaultTypeMap.MatchNamesWithUnderscores = true;

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Use CORS here (IMPORTANT: before MapControllers)
app.UseCors("AllowFrontend");

// Authentication reads the login token; authorization enforces [Authorize].
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Lets the test project host the app (WebApplicationFactory<Program>).
public partial class Program { }
