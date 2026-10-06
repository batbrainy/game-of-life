using System.Net;
using System.Text.Json;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class NextGenerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // A blinker alternates between these two forms, so a response that took no step or two steps, or that started
    // from a result an earlier call had saved, would show the vertical form.
    private const string VerticalBlinker = "[[0,1,0],[0,1,0],[0,1,0]]";
    private const string HorizontalBlinker = "[[0,0,0],[1,1,1],[0,0,0]]";

    [Fact]
    public async Task VerticalBlinkerReturnsTheHorizontalBlinkerAtGenerationOne()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, VerticalBlinker);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/next");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":1,"rows":3,"columns":3,"cells":{{HorizontalBlinker}}}""",
            await response.Content.ReadAsStringAsync());
    }

    // Two rows of three cells, so a response with rows and columns swapped would not match. Generation 1 differs
    // from the upload, and every generation after it has no live cells, so a response that took no step or more
    // than one would not match either.
    [Fact]
    public async Task TwoByThreeBoardReturnsItsNextGenerationAtTheSameSize()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1,1,0],[0,0,1]]");

        using var response = await client.GetAsync($"/api/v1/boards/{id}/next");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":1,"rows":2,"columns":3,"cells":[[0,1,0],[0,1,0]]}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TwoCallsReturnIdenticalBodies()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, VerticalBlinker);

        using var first = await client.GetAsync($"/api/v1/boards/{id}/next");
        using var second = await client.GetAsync($"/api/v1/boards/{id}/next");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FetchAfterwardsStillReturnsTheUploadAtGenerationZero()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, VerticalBlinker);
        using var next = await client.GetAsync($"/api/v1/boards/{id}/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        using var response = await client.GetAsync($"/api/v1/boards/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":0,"rows":3,"columns":3,"cells":{{VerticalBlinker}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownIdReturnsNotFoundProblemDetails()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync($"/api/v1/boards/{id}/next");

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

        using var response = await client.GetAsync("/api/v1/boards/not-a-guid/next");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bad Request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }
}