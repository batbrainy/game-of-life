using System.Globalization;
using System.Net;
using System.Text.Json;

using GameOfLife.Api.Configuration;

using Microsoft.AspNetCore.Mvc.Testing;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class GenerationsAheadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Glider = ".#./..#/###";

    // A block never changes, so its expected cells are the same at every generation.
    private const string BlockBoard = "[[1,1],[1,1]]";

    // Lowered to differ from the defaults of both generation limits, so a check that used a fixed number, or read
    // MaxFinalStateGenerations instead, would fail a test.
    private const int LoweredMaxGenerationsAhead = 5;

    // Taken from the defaults, so the tests of the default limit follow it if it changes.
    private static readonly int MaxGenerationsAhead = new GameOfLifeOptions().MaxGenerationsAhead;

    // Each generation of this glider up to 31 differs from the one before it, so a response one step short would not
    // match. From 31 on it stays a block in the bottom-right corner.
    private static readonly string GliderBoard = TestBoards.CellsJson(10, 10, 0, 0, Glider);

    public static TheoryData<int> GenerationsJustOutsideTheAllowedRange => new() { -1, MaxGenerationsAhead + 1 };

    [Fact]
    public async Task GenerationZeroReturnsTheSameBodyAsFetchingTheBoard()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, GliderBoard);

        using var fetched = await client.GetAsync($"/api/v1/boards/{id}");
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/0");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await fetched.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GenerationOneReturnsTheSameBodyAsTheNextGenerationEndpoint()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, GliderBoard);

        using var next = await client.GetAsync($"/api/v1/boards/{id}/next");
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/1");

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await next.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GliderMovesOneRowDownAndOneColumnRightInFourGenerations()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, GliderBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":4,"rows":10,"columns":10,"cells":{{TestBoards.CellsJson(10, 10, 1, 1, Glider)}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GliderOnATenByTenBoardIsABlockInTheCornerAfter31Generations()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, GliderBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":31,"rows":10,"columns":10,"cells":{{TestBoards.CellsJson(10, 10, 8, 8, "##/##")}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GenerationExactlyAtTheDefaultLimitIsReturned()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, BlockBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{MaxGenerationsAhead}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":{{MaxGenerationsAhead}},"rows":2,"columns":2,"cells":{{BlockBoard}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(GenerationsJustOutsideTheAllowedRange))]
    public async Task GenerationJustOutsideTheAllowedRangeReturnsAValidationProblemUnderN(int n)
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, BlockBoard);

        // Some cultures, such as sv-SE, write -1 with a Unicode minus sign, which does not parse as an int.
        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{n.ToString(CultureInfo.InvariantCulture)}");

        await AssertValidationProblemUnderNAsync(response, MaxGenerationsAhead, n);
    }

    [Fact]
    public async Task GenerationExactlyAtAConfiguredLimitIsReturned()
    {
        using var limitedFactory = WithLoweredLimit();
        using var client = limitedFactory.CreateClient();
        var id = await TestBoards.UploadAsync(client, BlockBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{LoweredMaxGenerationsAhead}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"id":"{{id}}","generation":{{LoweredMaxGenerationsAhead}},"rows":2,"columns":2,"cells":{{BlockBoard}}}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GenerationOneOverAConfiguredLimitReturnsAValidationProblemUnderN()
    {
        using var limitedFactory = WithLoweredLimit();
        using var client = limitedFactory.CreateClient();
        var id = await TestBoards.UploadAsync(client, BlockBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{LoweredMaxGenerationsAhead + 1}");

        await AssertValidationProblemUnderNAsync(response, LoweredMaxGenerationsAhead, LoweredMaxGenerationsAhead + 1);
    }

    // The id is not stored, so a 404 here would mean the board was looked up before n was checked.
    [Fact]
    public async Task GenerationOutsideTheAllowedRangeIsReportedBeforeAnUnknownId()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/boards/{Guid.NewGuid()}/generations/-1");

        await AssertValidationProblemUnderNAsync(response, MaxGenerationsAhead, -1);
    }

    // ASP.NET Core itself rejects an n that does not parse as an int, before the handler runs, so the body has no
    // errors member, unlike the response for an n outside the allowed range.
    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task GenerationThatIsNotAnIntReturnsBadRequestProblemDetails(string n)
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, BlockBoard);

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/{n}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownIdReturnsNotFoundProblemDetails()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync($"/api/v1/boards/{id}/generations/1");

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

        using var response = await client.GetAsync("/api/v1/boards/not-a-guid/generations/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bad Request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    private WebApplicationFactory<Program> WithLoweredLimit() =>
        factory.WithSettings(("GameOfLife:MaxGenerationsAhead", $"{LoweredMaxGenerationsAhead}"));

    private static async Task AssertValidationProblemUnderNAsync(HttpResponseMessage response, int limit, int n)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("n", error.Name);
        Assert.Equal(
            $"The generation must be 0 to {limit}; this request asks for {n}.",
            Assert.Single(error.Value.EnumerateArray()).GetString());
    }
}