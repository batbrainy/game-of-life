using System.Text.Json.Serialization;

using GameOfLife.Api.Configuration;
using GameOfLife.Api.Persistence.Migrations;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using Npgsql;

var builder = WebApplication.CreateBuilder(args);
bool isMigrateCommand = args.Length > 0 && args[0] == MigrateCommand.Name;
string? connectionString = builder.Configuration.GetConnectionString("GameOfLife");

// The migrate command reports a missing connection string itself, in its log and exit code.
if (string.IsNullOrWhiteSpace(connectionString) && !isMigrateCommand)
{
    throw new InvalidOperationException(
        "The connection string is missing. Set ConnectionStrings:GameOfLife, for example with the environment variable ConnectionStrings__GameOfLife.");
}

// One data source for the process, shared by the migrate command and the API; it owns the connection pool.
builder.Services.AddSingleton(services => new NpgsqlDataSourceBuilder(connectionString)
    .UseLoggerFactory(services.GetRequiredService<ILoggerFactory>())
    .Build());

builder.Services.AddOptions<GameOfLifeOptions>()
    .BindConfiguration(GameOfLifeOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails();
// The default is to throw in Development and return 400 elsewhere; return 400 everywhere.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
// Rejects numbers sent as strings, such as "1", instead of converting them.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (isMigrateCommand)
{
    // Disposing the app writes out buffered log messages and closes pooled connections before the process exits.
    await using (app)
    {
        return await MigrateCommand.RunAsync(app.Services, connectionString);
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Runs no health checks: it only shows that the process is serving requests.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();
return 0;

// Public so the test project can host the app through WebApplicationFactory<Program>.
public partial class Program;