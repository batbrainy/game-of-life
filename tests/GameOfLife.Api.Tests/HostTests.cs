using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace GameOfLife.Api.Tests;

public sealed class HostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetRootReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}