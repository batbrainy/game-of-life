using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using GameOfLife.Api.Endpoints;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class NextGenerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SuccessiveCallsPersistSuccessiveGenerations()
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[0,1,0],[0,1,0],[0,1,0]]");
        using var first = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        var one = await first.Content.ReadFromJsonAsync<BoardStateResponse>();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(one);
        Assert.Equal(1, one.Generation);
        Assert.Equal("[[0,0,0],[1,1,1],[0,0,0]]", JsonSerializer.Serialize(one.Cells));

        using var second = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        var two = await second.Content.ReadFromJsonAsync<BoardStateResponse>();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(two);
        Assert.Equal(2, two.Generation);
        Assert.Equal("Active", two.Status);
        Assert.Equal("[[0,1,0],[0,1,0],[0,1,0]]", JsonSerializer.Serialize(two.Cells));
        using var fetch = await client.GetAsync($"/api/v1/boards/{id}");
        Assert.Equal(await second.Content.ReadAsStringAsync(), await fetch.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000000", HttpStatusCode.NotFound)]
    public async Task InvalidOrUnknownIdsReturnProblemDetails(string id, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/v1/boards/{id}/next", null);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("final")]
    public async Task GetCannotMutateBoard(string operation)
    {
        using var client = factory.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1,1],[1,1]]");
        using var response = await client.GetAsync($"/api/v1/boards/{id}/{operation}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}