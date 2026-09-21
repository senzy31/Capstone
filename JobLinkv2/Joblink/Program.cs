using Dapper;
using Joblink.Security;
using Joblink.Services;
using Joblink.Services.Accounts;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Accounts;
using JobLinkv2.Services.Apply;
using JobLinkv2.Services.MyData;
using JobLinkv2.Services.Resumes;
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

// ✅ Notifications, saved jobs and matches (yours only), and the shared skills list
builder.Services.AddSingleton(new UserDataStore(connectionString));
builder.Services.AddSingleton(new SkillStore(connectionString));

// ✅ Apply flow
builder.Services.AddSingleton<IApplyStore>(new SqlApplyStore(connectionString));
builder.Services.AddSingleton<ApplyService>();
builder.Services.AddSingleton<ApplicationTrackerService>();
builder.Services.AddSingleton<JobImportService>();

// ✅ Add CORS here
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy
                .AllowAnyOrigin()   // or specify your frontend URL
                .AllowAnyHeader()
                .AllowAnyMethod();
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
