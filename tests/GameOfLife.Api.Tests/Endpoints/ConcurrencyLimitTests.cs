using System.Net;
using System.Net.Http.Headers;
using System.Text;

using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class ConcurrencyLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Every wait ends after this long, so a request that is queued instead of turned away, or a held request that is
    // never released, fails the test instead of hanging the run.
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("final")]
    [InlineData("next")]
    [InlineData("generations/5")]
    public async Task SimulationRequestWhileTheBoundIsReachedReturnsServerBusyProblemDetailsWithRetryAfter(string simulation)
    {
        var repository = new HoldingBoardRepository(requestsToHold: 1);
        using var boundedFactory = WithBound(1, repository);
        using var client = boundedFactory.CreateClient();
        var heldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        await repository.Entered.WaitAsync(TimeLimit);

        using var response = await SendSimulationAsync(client, $"/api/v1/boards/{Guid.NewGuid()}/{simulation}", simulation).WaitAsync(TimeLimit);
        repository.Release();
        using var heldResponse = await heldRequest.WaitAsync(TimeLimit);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Server busy","status":503,"detail":"The server is handling as many simulation requests as it allows. Try again shortly."}""",
            await response.Content.ReadAsStringAsync());
    }

    // Problem details are JSON, which this request rules out. It must still get 503 and Retry-After, in the plain text
    // that UseStatusCodePages writes, rather than a 500 from failing to write problem details.
    [Fact]
    public async Task TurnedAwayRequestThatAcceptsOnlyHtmlGetsPlainTextServiceUnavailableWithRetryAfter()
    {
        var repository = new HoldingBoardRepository(requestsToHold: 1);
        using var boundedFactory = WithBound(1, repository);
        using var client = boundedFactory.CreateClient();
        var heldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        await repository.Entered.WaitAsync(TimeLimit);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/boards/{Guid.NewGuid()}/final");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request).WaitAsync(TimeLimit);
        repository.Release();
        using var heldResponse = await heldRequest.WaitAsync(TimeLimit);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task UploadFetchAndLivenessSucceedWhileTheBoundIsReached()
    {
        var repository = new HoldingBoardRepository(requestsToHold: 1);
        using var boundedFactory = WithBound(1, repository);
        using var client = boundedFactory.CreateClient();
        var heldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        await repository.Entered.WaitAsync(TimeLimit);

        using var upload = new StringContent("""{ "cells": [[1,1],[1,1]] }""", Encoding.UTF8, "application/json");
        using var uploaded = await client.PostAsync("/api/v1/boards", upload).WaitAsync(TimeLimit);
        using var fetched = await client.GetAsync($"/api/v1/boards/{Guid.NewGuid()}").WaitAsync(TimeLimit);
        using var live = await client.GetAsync("/health/live").WaitAsync(TimeLimit);
        repository.Release();
        using var heldResponse = await heldRequest.WaitAsync(TimeLimit);

        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task SimulationRequestSucceedsOnceTheHeldRequestHasCompleted()
    {
        var repository = new HoldingBoardRepository(requestsToHold: 1);
        using var boundedFactory = WithBound(1, repository);
        using var client = boundedFactory.CreateClient();
        var heldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        await repository.Entered.WaitAsync(TimeLimit);
        using var whileHeld = await client.PostAsync($"/api/v1/boards/{Guid.NewGuid()}/final", null).WaitAsync(TimeLimit);
        repository.Release();
        using var heldResponse = await heldRequest.WaitAsync(TimeLimit);

        using var afterwards = await client.PostAsync($"/api/v1/boards/{Guid.NewGuid()}/final", null).WaitAsync(TimeLimit);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, whileHeld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, heldResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
    }

    // With a bound of 2 as well as the other tests' 1, a limiter that used a fixed number or the number of processors
    // instead of the setting fails a test in this class on any machine.
    [Fact]
    public async Task BoundOfTwoLetsTwoSimulationRequestsRunAtOnceAndTurnsAwayAThird()
    {
        var repository = new HoldingBoardRepository(requestsToHold: 2);
        using var boundedFactory = WithBound(2, repository);
        using var client = boundedFactory.CreateClient();
        var firstHeldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        var secondHeldRequest = client.PostAsync($"/api/v1/boards/{HoldingBoardRepository.HeldId}/final", null);
        await repository.Entered.WaitAsync(TimeLimit);

        using var thirdResponse = await client.PostAsync($"/api/v1/boards/{Guid.NewGuid()}/final", null).WaitAsync(TimeLimit);
        repository.Release();
        using var firstHeldResponse = await firstHeldRequest.WaitAsync(TimeLimit);
        using var secondHeldResponse = await secondHeldRequest.WaitAsync(TimeLimit);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, thirdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstHeldResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondHeldResponse.StatusCode);
    }

    [Fact]
    public void StartupFailsWhenMaxConcurrentSimulationsIsBelowOne()
    {
        using var invalidFactory = factory.WithSettings(("GameOfLife:MaxConcurrentSimulations", "0"));

        var exception = Assert.Throws<OptionsValidationException>(() => invalidFactory.CreateClient());
        Assert.Contains("MaxConcurrentSimulations", exception.Message);
    }

    private static Task<HttpResponseMessage> SendSimulationAsync(HttpClient client, string url, string operation) =>
        operation.StartsWith("generations/", StringComparison.Ordinal) ? client.GetAsync(url) : client.PostAsync(url, null);

    // A copy of this class's host that handles at most the given number of simulation requests at once and reads
    // boards from the given repository.
    private WebApplicationFactory<Program> WithBound(int bound, HoldingBoardRepository repository) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GameOfLife:MaxConcurrentSimulations", $"{bound}");
            builder.ConfigureTestServices(services => services.AddSingleton<IBoardRepository>(repository));
        });

    // Holds FindAsync for HeldId until Release is called, so each request for that board keeps its permit for as long
    // as a test needs; the tests release them before their assertions, so they finish even when one fails. Any other
    // id finds a 2 x 2 block at once, and AddAsync stores nothing.
    private sealed class HoldingBoardRepository(int requestsToHold) : IBoardRepository
    {
        public static readonly Guid HeldId = Guid.NewGuid();

        private static readonly Board Block = Board.FromCells(new bool[,] { { true, true }, { true, true } });

        // Completing one of these does not run the code that waits on it, in the test or in the server, on the
        // completing thread.
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _heldRequestCount;

        // Completes once requestsToHold requests for HeldId are inside the endpoint, each holding a permit.
        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        public Task AddAsync(Guid id, Board board, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IBoardMutationSession> LockAsync(Guid id, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<IBoardMutationSession>(new HoldingSession(this, id));

        private sealed class HoldingSession(HoldingBoardRepository repository, Guid id) : IBoardMutationSession
        {
            public Task<StoredBoard?> FindAsync(CancellationToken cancellationToken) => repository.FindAsync(id, cancellationToken);

            public Task<StoredBoard> SaveAsync(Board board, long generation, BoardStatus status, long? cycleStartGeneration, int? period,
                CancellationToken cancellationToken) => Task.FromResult(new StoredBoard(
                    id, board, generation, status, DateTime.UnixEpoch, DateTime.UnixEpoch, null, cycleStartGeneration, period));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        public async Task<StoredBoard?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id == HeldId)
            {
                // Two held requests can enter at the same moment on different threads, where a plain ++ could count
                // only one of them.
                if (Interlocked.Increment(ref _heldRequestCount) == requestsToHold)
                {
                    _entered.TrySetResult();
                }

                await _released.Task.WaitAsync(TimeLimit, cancellationToken);
            }

            return new StoredBoard(id, Block, 0, BoardStatus.Active, DateTime.UnixEpoch, DateTime.UnixEpoch);
        }
    }
}