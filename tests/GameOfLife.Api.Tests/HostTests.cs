using System.Net;

using GameOfLife.Api.Configuration;
using GameOfLife.Api.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Tests;

[Collection(PostgresFixture.CollectionName)]
public sealed class HostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LivenessProbeReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFoundProblemDetails()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnhandledExceptionReturnsProblemDetailsWithoutItsMessage()
    {
        const string Message = "internal detail that must not reach the client";
        using var throwingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddSingleton<IStartupFilter>(new ThrowingMiddleware(Message))));
        using var client = throwingFactory.CreateClient();

        using var response = await client.GetAsync(ThrowingMiddleware.Path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(Message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void StartupFailsWhenALimitIsBelowOne()
    {
        using var invalidFactory = factory.WithSettings(("GameOfLife:MaxRows", "0"));

        var exception = Assert.Throws<OptionsValidationException>(() => invalidFactory.CreateClient());
        Assert.Contains("MaxRows", exception.Message);
    }

    [Fact]
    public void StartupFailsWithoutAConnectionString()
    {
        using var unconfiguredFactory = factory.WithSettings(("ConnectionStrings:GameOfLife", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => unconfiguredFactory.CreateClient());
        Assert.Contains("ConnectionStrings:GameOfLife", exception.Message);
    }

    [Fact]
    public void BoardRepositoryIsOneSharedNpgsqlInstance()
    {
        var first = factory.Services.GetRequiredService<IBoardRepository>();
        var second = factory.Services.GetRequiredService<IBoardRepository>();

        Assert.IsType<NpgsqlBoardRepository>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void StartupRaisesTheThreadPoolMinimumByTheDefaultSimulationBound()
    {
        using var defaultFactory = factory.WithSettings();

        int minimum = MinimumWorkerThreadsAfterStartup(defaultFactory, startingMinimum: Environment.ProcessorCount);

        Assert.Equal(Environment.ProcessorCount + new GameOfLifeOptions().MaxConcurrentSimulations, minimum);
    }

    [Fact]
    public void StartupRaisesTheThreadPoolMinimumByAConfiguredSimulationBound()
    {
        using var boundedFactory = factory.WithSettings(("GameOfLife:MaxConcurrentSimulations", "3"));

        int minimum = MinimumWorkerThreadsAfterStartup(boundedFactory, startingMinimum: Environment.ProcessorCount);

        Assert.Equal(Environment.ProcessorCount + 3, minimum);
    }

    [Fact]
    public void StartupKeepsAHigherThreadPoolMinimum()
    {
        using var boundedFactory = factory.WithSettings(("GameOfLife:MaxConcurrentSimulations", "3"));
        int higherMinimum = Environment.ProcessorCount + 100;

        int minimum = MinimumWorkerThreadsAfterStartup(boundedFactory, startingMinimum: higherMinimum);

        Assert.Equal(higherMinimum, minimum);
    }

    // The minimum belongs to the whole test process, and other hosts started in it can have raised it already, so it is
    // set to a known value before the host starts and put back afterwards. hostFactory must be a new factory, so that
    // its host starts here.
    private static int MinimumWorkerThreadsAfterStartup(WebApplicationFactory<Program> hostFactory, int startingMinimum)
    {
        ThreadPool.GetMinThreads(out int originalMinimum, out int completionPortMinimum);
        ThreadPool.SetMinThreads(startingMinimum, completionPortMinimum);
        try
        {
            using var client = hostFactory.CreateClient();
            ThreadPool.GetMinThreads(out int minimumAfterStartup, out _);
            return minimumAfterStartup;
        }
        finally
        {
            ThreadPool.SetMinThreads(originalMinimum, completionPortMinimum);
        }
    }

    // Added after the application's own pipeline, so its exception travels back through the
    // exception handler the same way an exception from an endpoint would.
    private sealed class ThrowingMiddleware(string message) : IStartupFilter
    {
        public const string Path = "/test/throw";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException(message)));
        };
    }
}