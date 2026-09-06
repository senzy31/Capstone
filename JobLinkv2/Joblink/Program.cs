using Dapper;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.UseAuthorization();

app.MapControllers();

app.Run();