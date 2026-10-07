using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace GameOfLife.Api.Tests.Errors;

[Collection(PostgresFixture.CollectionName)]
public sealed class DatabaseUnavailableTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Nothing listens on port 872 of this host, and neither value appears in a fixed part of the body (its type link,
    // title, status and detail), so finding one there means it leaked. Port 1, used by other tests, would not do:
    // the type link contains a 1.
    private const string UnreachableHost = "127.0.0.1";
    private const string UnreachablePort = "872";

    [Fact]
    public async Task UploadToAnUnreachableDatabaseReturnsServiceUnavailableProblemDetails()
    {
        using var unreachableFactory = WithUnreachableDatabase();
        using var client = unreachableFactory.CreateClient();
        using var content = new StringContent("""{ "cells": [[0,1,0],[0,1,0],[0,1,0]] }""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/boards", content);

        await AssertDatabaseUnavailableAsync(response);
    }

    [Fact]
    public async Task FetchFromAnUnreachableDatabaseReturnsServiceUnavailableProblemDetails()
    {
        using var unreachableFactory = WithUnreachableDatabase();
        using var client = unreachableFactory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/boards/{Guid.NewGuid()}");

        await AssertDatabaseUnavailableAsync(response);
    }

    [Fact]
    public async Task RepositoryExceptionOtherThanAnOutageReturnsInternalServerErrorProblemDetails()
    {
        await AssertInternalServerErrorAsync(new InvalidOperationException("A failure that is not a database outage."));
    }

    [Fact]
    public async Task DatabaseErrorThatIsNotTransientReturnsInternalServerErrorProblemDetails()
    {
        // No inner exception, so IsTransient is false: NpgsqlException decides it from its inner exception alone.
        await AssertInternalServerErrorAsync(new NpgsqlException("A database error that a retry would not fix."));
    }

    private WebApplicationFactory<Program> WithUnreachableDatabase() => factory.WithSettings(
        ("ConnectionStrings:GameOfLife", $"Host={UnreachableHost};Port={UnreachablePort};Username=unused;Database=unused"));

    private static async Task AssertDatabaseUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = Assert.IsType<JsonObject>(JsonNode.Parse(await response.Content.ReadAsStringAsync()));
        Assert.Equal("Database unavailable", problem["title"]?.GetValue<string>());
        Assert.Equal(503, problem["status"]?.GetValue<int>());
        // An exact match, because exception text such as "Connection refused" names neither the host nor the port.
        Assert.Equal("The database is temporarily unavailable. Try again later.", problem["detail"]?.GetValue<string>());

        // A trace id, if the body has one, is random hexadecimal and could contain 872 by chance.
        problem.Remove("traceId");
        string bodyWithoutTraceId = problem.ToJsonString();
        Assert.DoesNotContain(UnreachableHost, bodyWithoutTraceId);
        Assert.DoesNotContain(UnreachablePort, bodyWithoutTraceId);
    }

    private async Task AssertInternalServerErrorAsync(Exception repositoryException)
    {
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<IBoardRepository>(new ThrowingBoardRepository(repositoryException))));
        using var client = failingFactory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/boards/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = Assert.IsType<JsonObject>(JsonNode.Parse(await response.Content.ReadAsStringAsync()));
        Assert.Equal(500, problem["status"]?.GetValue<int>());
        Assert.NotEqual("Database unavailable", problem["title"]?.GetValue<string>());
    }

    // Fails every call with the exception it was given.
    private sealed class ThrowingBoardRepository(Exception exception) : IBoardRepository
    {
        public Task AddAsync(Guid id, Board board, CancellationToken cancellationToken) => throw exception;

        public Task<IBoardMutationSession> LockAsync(Guid id, TimeSpan timeout, CancellationToken cancellationToken) => throw exception;

        public Task<StoredBoard?> FindAsync(Guid id, CancellationToken cancellationToken) => throw exception;
    }
}