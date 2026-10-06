using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using GameOfLife.Api.Configuration;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class FinalStateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Glider = ".#./..#/###";
    private const string RPentomino = ".##/##./.#.";

    [Fact]
    public async Task BlockIsAStillLifeFromGenerationZero()
    {
        using var client = factory.CreateClient();
        string block = TestBoards.CellsJson(4, 4, 1, 1, "##/##");
        var id = await TestBoards.UploadAsync(client, block);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":0,"period":1,"rows":4,"columns":4,"cells":{{block}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VerticalBlinkerOscillatesWithPeriodTwoFromGenerationZero()
    {
        using var client = factory.CreateClient();
        string blinker = TestBoards.CellsJson(3, 3, 0, 1, "#/#/#");
        var id = await TestBoards.UploadAsync(client, blinker);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":0,"period":2,"rows":3,"columns":3,"cells":{{blinker}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GliderOnATenByTenBoardSettlesAsABlockInTheBottomRightCornerAtGeneration31()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(10, 10, 0, 0, Glider));

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string block = TestBoards.CellsJson(10, 10, 8, 8, "##/##");
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":31,"period":1,"rows":10,"columns":10,"cells":{{block}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SingleCellDiesAndTheEmptyBoardIsAStillLifeFromGenerationOne()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(3, 3, 1, 1, "#"));

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":1,"period":1,"rows":3,"columns":3,"cells":[[0,0,0],[0,0,0],[0,0,0]]}""",
            await response.Content.ReadAsStringAsync());
    }

    // The other boards in this class are square, so swapping the rows and columns values would not fail them.
    [Fact]
    public async Task BlockOnAFourBySixBoardReturnsFourRowsAndSixColumns()
    {
        using var client = factory.CreateClient();
        string block = TestBoards.CellsJson(4, 6, 1, 2, "##/##");
        var id = await TestBoards.UploadAsync(client, block);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":0,"period":1,"rows":4,"columns":6,"cells":{{block}}}""",
            await response.Content.ReadAsStringAsync());
    }

    // The glider is a block from generation 31, but only generation 32 repeats it, so the search needs a limit of 32.
    [Fact]
    public async Task GliderWithALimitOf31GenerationsReturnsUnprocessableEntityProblemDetails()
    {
        using var limitedFactory = factory.WithSettings(("GameOfLife:MaxFinalStateGenerations", "31"));
        using var client = limitedFactory.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(10, 10, 0, 0, Glider));

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        await AssertNoFinalStateProblemAsync(response, maxGenerations: 31);
    }

    [Fact]
    public async Task GliderWithALimitOf32GenerationsReturnsTheBlock()
    {
        using var limitedFactory = factory.WithSettings(("GameOfLife:MaxFinalStateGenerations", "32"));
        using var client = limitedFactory.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(10, 10, 0, 0, Glider));

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string block = TestBoards.CellsJson(10, 10, 8, 8, "##/##");
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":31,"period":1,"rows":10,"columns":10,"cells":{{block}}}""",
            await response.Content.ReadAsStringAsync());
    }

    // The R-pentomino's cycle on this board starts at generation 1163 with period 2, so it first repeats at 1165; the
    // test needs a default limit below that.
    [Fact]
    public async Task RPentominoOnA68By68BoardReturnsUnprocessableEntityProblemDetailsAtTheDefaultLimit()
    {
        int defaultLimit = new GameOfLifeOptions().MaxFinalStateGenerations;
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, TestBoards.CellsJson(68, 68, 32, 32, RPentomino));

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

        await AssertNoFinalStateProblemAsync(response, defaultLimit);
    }

    [Fact]
    public async Task BoardThatDoesNotSettleIsLoggedAtInformationWithItsIdAndTheLimitButOneThatSettlesIsNot()
    {
        var logs = new RecordingLoggerProvider();
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GameOfLife:MaxFinalStateGenerations", "31");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = limitedFactory.CreateClient();
        var gliderId = await TestBoards.UploadAsync(client, TestBoards.CellsJson(10, 10, 0, 0, Glider));
        var blockId = await TestBoards.UploadAsync(client, TestBoards.CellsJson(4, 4, 1, 1, "##/##"));

        using var gliderResponse = await client.GetAsync($"/api/v1/boards/{gliderId}/final");
        using var blockResponse = await client.GetAsync($"/api/v1/boards/{blockId}/final");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, gliderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blockResponse.StatusCode);
        Assert.Contains($"Information: Board {gliderId} did not repeat an earlier generation within 31 generations", logs.Messages);
        Assert.DoesNotContain(logs.Messages, message => message.Contains($"Board {blockId} did not repeat"));
    }

    [Fact]
    public async Task UnknownIdReturnsNotFoundProblemDetails()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync($"/api/v1/boards/{id}/final");

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

        using var response = await client.GetAsync("/api/v1/boards/not-a-guid/final");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bad Request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task AssertNoFinalStateProblemAsync(HttpResponseMessage response, int maxGenerations)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Board did not reach a final state", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(422, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains($"{maxGenerations}", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(maxGenerations, problem.RootElement.GetProperty("maxGenerations").GetInt32());
    }

    // Keeps every message the host logs as "Level: message". A concurrent queue, because the host can log from
    // several threads at once.
    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue($"{logLevel}: {formatter(state, exception)}");

        public void Dispose()
        {
        }
    }
}