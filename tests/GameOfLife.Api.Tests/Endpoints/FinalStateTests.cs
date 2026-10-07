using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using GameOfLife.Api.Endpoints;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class FinalStateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("[[1,1],[1,1]]", "Stable", 1, 0, 1)]
    [InlineData("[[0,1,0],[0,1,0],[0,1,0]]", "Cycle", 2, 0, 2)]
    [InlineData("[[1]]", "Stable", 2, 1, 1)]
    public async Task FinalPersistsDetectionGenerationAndTerminalMetadata(string cells, string status, long generation, long cycleStart, int period)
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, cells);
        using var response = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var final = await response.Content.ReadFromJsonAsync<BoardStateResponse>();
        Assert.NotNull(final);
        Assert.Equal(status, final.Status);
        Assert.Equal(generation, final.Generation);
        Assert.Equal(cycleStart, final.CycleStartGeneration);
        Assert.Equal(period, final.Period);
        using var fetch = await client.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(await response.Content.ReadAsStringAsync(), await fetch.Content.ReadAsStringAsync());
        using var again = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        using var rejected = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(status, problem.RootElement.GetProperty("boardStatus").GetString());
        Assert.Equal($"/api/v1/boards/{id}", problem.RootElement.GetProperty("finalStateUrl").GetString());
    }

    [Fact]
    public async Task FinalStartsAtCurrentGenerationAndTerminalStateSurvivesANewHost()
    {
        Guid id;
        string completed;
        using (var first = factory.WithSettings())
        {
            using var client = first.CreateClient();
            id = await TestBoards.UploadAsync(client, "[[0,1,0],[0,1,0],[0,1,0]]");
            using var next = await client.PostAsync($"/api/v1/boards/{id}/next", null);
            next.EnsureSuccessStatusCode();
            using var final = await client.PostAsync($"/api/v1/boards/{id}/final", null);
            var state = await final.Content.ReadFromJsonAsync<BoardStateResponse>();
            Assert.NotNull(state);
            Assert.Equal(3, state.Generation);
            Assert.Equal(1, state.CycleStartGeneration);
            Assert.Equal("[[0,0,0],[1,1,1],[0,0,0]]", JsonSerializer.Serialize(state.Cells));
            completed = await final.Content.ReadAsStringAsync();
        }

        using var second = factory.WithSettings();
        using var secondClient = second.CreateClient();
        using var fetched = await secondClient.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(completed, await fetched.Content.ReadAsStringAsync());
        using var nextAfterRestart = await secondClient.PostAsync($"/api/v1/boards/{id}/next", null);
        Assert.Equal(HttpStatusCode.Conflict, nextAfterRestart.StatusCode);
    }

    [Fact]
    public async Task IterationLimitSavesNoPartialProgressAndReleasesLock()
    {
        using var limited = factory.WithSettings(("GameOfLife:MaxFinalStateGenerations", "1"));
        using var client = limited.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1]]");
        using var initial = await client.GetAsync($"/api/v1/boards/{id}");
        using var failed = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        using var problem = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        Assert.Equal(1, problem.RootElement.GetProperty("maxGenerations").GetInt32());
        using var after = await client.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(await initial.Content.ReadAsStringAsync(), await after.Content.ReadAsStringAsync());
        using var next = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        using var final = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
    }

    [Theory]
    [InlineData(31, HttpStatusCode.UnprocessableEntity)]
    [InlineData(32, HttpStatusCode.OK)]
    public async Task TransientPatternNeedsTheDetectionStepWithinTheLimit(int limit, HttpStatusCode expected)
    {
        using var bounded = factory.WithSettings(("GameOfLife:MaxFinalStateGenerations", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var client = bounded.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(10, 10, 0, 0, ".#./..#/###"));
        using var final = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(expected, final.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var state = await final.Content.ReadFromJsonAsync<BoardStateResponse>();
            Assert.NotNull(state);
            Assert.Equal(32, state.Generation);
            Assert.Equal(31, state.CycleStartGeneration);
            Assert.Equal("Stable", state.Status);
        }
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000000", HttpStatusCode.NotFound)]
    public async Task InvalidOrUnknownIdsReturnProblemDetails(string id, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}