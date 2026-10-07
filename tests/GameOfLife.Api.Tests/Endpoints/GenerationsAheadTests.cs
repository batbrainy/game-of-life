using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using GameOfLife.Api.Endpoints;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class GenerationsAheadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(0, "[[0,0,0],[1,1,1],[0,0,0]]")]
    [InlineData(1, "[[0,1,0],[0,1,0],[0,1,0]]")]
    [InlineData(2, "[[0,0,0],[1,1,1],[0,0,0]]")]
    public async Task ProjectionStartsFromCurrentStateAndDoesNotPersist(int n, string expectedCells)
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[0,1,0],[0,1,0],[0,1,0]]");
        using var advanced = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        advanced.EnsureSuccessStatusCode();
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{n}");
        var projection = await response.Content.ReadFromJsonAsync<BoardProjectionResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(projection);
        Assert.Equal(1, projection.SourceGeneration);
        Assert.Equal(1 + n, projection.Generation);
        Assert.Equal(expectedCells, JsonSerializer.Serialize(projection.Cells));
        using var fetched = await client.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(await advanced.Content.ReadAsStringAsync(), await fetched.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TerminalBoardCanBeProjectedWithoutChangingItsStoredState()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[0,1,0],[0,1,0],[0,1,0]]");
        using var final = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        using var projected = await client.GetAsync($"/api/v1/boards/{id}/generations/1");
        var result = await projected.Content.ReadFromJsonAsync<BoardProjectionResponse>();
        Assert.NotNull(result);
        Assert.Equal(2, result.SourceGeneration);
        Assert.Equal(3, result.Generation);
        Assert.Equal("[[0,0,0],[1,1,1],[0,0,0]]", JsonSerializer.Serialize(result.Cells));
        using var fetched = await client.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(await final.Content.ReadAsStringAsync(), await fetched.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("6")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task InvalidNReturnsBadRequest(string n)
    {
        using var bounded = factory.WithSettings(("GameOfLife:MaxGenerationsAhead", "5"));
        using var client = bounded.CreateClient();
        using var response = await client.GetAsync($"/api/v1/boards/{Guid.NewGuid()}/generations/{n}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000000", HttpStatusCode.NotFound)]
    public async Task InvalidOrUnknownIdReturnsProblemDetails(string id, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/1");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LoweredLimitsPreventExpensiveSimulationOfAnExistingLargerBoard()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1,1],[1,1]]");
        using var smaller = factory.WithSettings(("GameOfLife:MaxRows", "1"));
        using var limitedClient = smaller.CreateClient();
        using var projection = await limitedClient.GetAsync($"/api/v1/boards/{id}/generations/1");
        using var next = await limitedClient.PostAsync($"/api/v1/boards/{id}/next", null);
        using var fetched = await limitedClient.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, projection.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, next.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task ConfiguredMaximumIsInclusive()
    {
        using var bounded = factory.WithSettings(("GameOfLife:MaxGenerationsAhead", "5"));
        using var client = bounded.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1,1],[1,1]]");
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}