using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace GameOfLife.Api.Tests;

[Collection(PostgresFixture.CollectionName)]
public sealed class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The status codes are in ascending order, because the test compares them with the document's codes sorted.
    public static TheoryData<string, string, string, string, string[]> BoardOperations => new()
    {
        { "/api/v1/boards", "post", "UploadBoard", "Upload a board", ["201", "400"] },
        { "/api/v1/boards/{id}", "get", "GetBoard", "Get a stored board", ["200", "400", "404"] },
        { "/api/v1/boards/{id}/next", "get", "GetNextGeneration", "Get the next generation of a stored board", ["200", "400", "404"] },
        { "/api/v1/boards/{id}/generations/{n}", "get", "GetGeneration", "Get a stored board n generations ahead", ["200", "400", "404"] },
        { "/api/v1/boards/{id}/final", "get", "GetFinalState", "Get the final state of a stored board", ["200", "400", "404", "422"] },
    };

    [Theory]
    [MemberData(nameof(BoardOperations))]
    public async Task DocumentInDevelopmentDescribesTheBoardOperationAndExactlyItsResponses(
        string path, string method, string operationId, string summary, string[] statusCodes)
    {
        using var developmentFactory = WithEnvironment(Environments.Development);
        using var client = developmentFactory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal(summary, operation.GetProperty("summary").GetString());
        var documentedResponses = operation.GetProperty("responses").EnumerateObject().ToList();
        Assert.Equal(statusCodes, documentedResponses.Select(documented => documented.Name).Order());
        foreach (var documented in documentedResponses)
        {
            // What the API sends: the result as JSON on success, and problem details for every error.
            string expectedMediaType = documented.Name.StartsWith('2') ? "application/json" : "application/problem+json";
            var mediaType = Assert.Single(documented.Value.GetProperty("content").EnumerateObject());
            Assert.Equal(expectedMediaType, mediaType.Name);
        }
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json", "Production")]
    [InlineData("/swagger/index.html", "Production")]
    [InlineData("/swagger/v1/swagger.json", "Staging")]
    [InlineData("/swagger/index.html", "Staging")]
    public async Task SwaggerPathIsServedInDevelopmentAndReturnsNotFoundInOtherEnvironments(
        string path, string otherEnvironment)
    {
        using var developmentFactory = WithEnvironment(Environments.Development);
        using var otherFactory = WithEnvironment(otherEnvironment);
        using var developmentClient = developmentFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();

        using var developmentResponse = await developmentClient.GetAsync(path);
        using var otherResponse = await otherClient.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, developmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    // ApiFactory always hosts the API in Production; this copy of it runs in the given environment instead.
    private WebApplicationFactory<Program> WithEnvironment(string environment) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
}