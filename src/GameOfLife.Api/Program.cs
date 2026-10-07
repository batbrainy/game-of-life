using System.Text.Json.Serialization;

using GameOfLife.Api.Configuration;
using GameOfLife.Api.Endpoints;
using GameOfLife.Api.Errors;
using GameOfLife.Api.Health;
using GameOfLife.Api.Persistence;
using GameOfLife.Api.Persistence.Migrations;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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
builder.Services.AddSingleton<IBoardRepository, NpgsqlBoardRepository>();

builder.Services.AddOptions<GameOfLifeOptions>()
    .BindConfiguration(GameOfLifeOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseUnavailableExceptionHandler>();
// The default is to throw in Development and return 400 elsewhere; return 400 everywhere.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
// Rejects numbers sent as strings, such as "1", instead of converting them.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
    options.OnRejected = SimulationConcurrencyLimit.WriteServerBusyAsync;
});
// AddConcurrencyLimiter gives every request the same partition, so all of them share one limiter and the bound is
// for the whole server, not for each client. The permit limit is read once, from the validated GameOfLife options.
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<IOptions<GameOfLifeOptions>>((options, limits) => options.AddConcurrencyLimiter(
        SimulationConcurrencyLimit.PolicyName,
        concurrency =>
        {
            concurrency.PermitLimit = limits.Value.MaxConcurrentSimulations;
            concurrency.QueueLimit = 0;
        }));

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

// Each running simulation keeps a thread-pool thread busy until it ends. By default the pool's minimum number of
// worker threads is the processor count, the same as the default MaxConcurrentSimulations, and once that many are
// busy the pool can wait for running work to finish before it adds a thread. Raising the minimum to the bound plus
// one thread per processor lets other requests, including those answered with 503 and the health checks, get a
// thread at once.
int simulationBound = app.Services.GetRequiredService<IOptions<GameOfLifeOptions>>().Value.MaxConcurrentSimulations;
int neededMinimum = Environment.ProcessorCount + simulationBound;
ThreadPool.GetMinThreads(out int currentMinimum, out int completionPortMinimum);
if (currentMinimum < neededMinimum)
{
    ThreadPool.SetMinThreads(neededMinimum, completionPortMinimum);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapBoardEndpoints();

// Runs no health checks: it only shows that the process is serving requests.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Writes no body, so UseStatusCodePages answers a 503 with problem details, as for other errors. The default writer
// would send plain text, and a 200 needs no body.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = (_, _) => Task.CompletedTask,
});

app.Run();
return 0;

// Public so the test project can host the app through WebApplicationFactory<Program>.
public partial class Program;