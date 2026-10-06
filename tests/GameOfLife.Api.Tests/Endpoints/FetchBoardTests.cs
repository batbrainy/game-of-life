using System.Net;
using System.Text;
using System.Text.Json;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class FetchBoardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Two rows of three cells, with no symmetry: a response with rows and columns swapped, or with the rows or
    // the cells in a row reversed, would not match it.
    private const string UploadJson = """{ "cells": [[1,1,0],[0,0,1]] }""";

    [Fact]
    public async Task UploadedBoardIsReturnedAtGenerationZeroWithItsSizeAndCells()
    {
        using var client = factory.CreateClient();
        using var upload = await UploadAsync(client);
        var id = await ReadIdAsync(upload);

        using var response = await client.GetAsync($"/api/v1/boards/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":0,"rows":2,"columns":3,"cells":[[1,1,0],[0,0,1]]}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LocationOfAnUploadReturnsTheStoredBoard()
    {
        using var client = factory.CreateClient();
        using var upload = await UploadAsync(client);
        var id = await ReadIdAsync(upload);

        using var response = await client.GetAsync(upload.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, await ReadIdAsync(response));
    }

    [Fact]
    public async Task UnknownIdReturnsNotFoundProblemDetails()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync($"/api/v1/boards/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Board not found", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains(id.ToString(), problem.RootElement.GetProperty("detail").GetString());
    }

    // ASP.NET Core itself rejects an id that does not parse as a GUID, before the handler runs.
    [Fact]
    public async Task MalformedIdReturnsBadRequestProblemDetails()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/boards/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bad Request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    // Each instance is a new host, with its own services and connection pool, on this class's database. The first
    // is disposed before the second starts, so only the database can carry the board across.
    [Fact]
    public async Task BoardUploadedBeforeARestartIsReturnedUnchangedAfterIt()
    {
        Guid id;
        string bodyBeforeRestart;
        using (var firstInstance = factory.WithSettings())
        {
            using var client = firstInstance.CreateClient();
            using var upload = await UploadAsync(client);
            id = await ReadIdAsync(upload);
            using var response = await client.GetAsync($"/api/v1/boards/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodyBeforeRestart = await response.Content.ReadAsStringAsync();
        }

        using var secondInstance = factory.WithSettings();
        using var secondClient = secondInstance.CreateClient();
        using var responseAfterRestart = await secondClient.GetAsync($"/api/v1/boards/{id}");

        Assert.Equal(HttpStatusCode.OK, responseAfterRestart.StatusCode);
        Assert.Equal(bodyBeforeRestart, await responseAfterRestart.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client)
    {
        using var content = new StringContent(UploadJson, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/boards", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response;
    }

    // Reads "id" from the body of an upload or of a fetched board; both have it.
    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}