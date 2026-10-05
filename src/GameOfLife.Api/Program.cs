using System.Text.Json.Serialization;

using GameOfLife.Api.Configuration;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("GameOfLife")))
{
    throw new InvalidOperationException(
        "The connection string is missing. Set ConnectionStrings:GameOfLife, for example with the environment variable ConnectionStrings__GameOfLife.");
}

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

// Public so the test project can host the app through WebApplicationFactory<Program>.
public partial class Program;