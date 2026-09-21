using Dapper;
using Joblink.Security;
using Joblink.Services;
using JobLinkv2.Repositories;
using JobLinkv2.Services.Apply;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

// ✅ Apply flow
builder.Services.AddSingleton<IApplyStore>(new SqlApplyStore(
    builder.Configuration.GetConnectionString("Joblink") ?? DbConfig.DefaultConnectionString));
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
