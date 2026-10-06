using System.Net;
using System.Text;
using System.Text.Json;

using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GameOfLife.Api.Tests.Endpoints;

[Collection(PostgresFixture.CollectionName)]
public sealed class UploadBoardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Lowered so the grids in the limit tests stay small. The two differ, so a check that compared the number
    // of rows with MaxColumns, or the length of a row with MaxRows, would fail a test.
    private const int LoweredMaxRows = 3;
    private const int LoweredMaxColumns = 4;

    [Fact]
    public async Task ValidThreeByThreeBoardIsCreatedAndStoredUnderTheReturnedId()
    {
        using var client = factory.CreateClient();
        // The glider is not symmetric, so a board stored transposed or flipped would not equal it.
        var glider = Board.FromCells(new bool[,]
        {
            { false, true, false },
            { false, false, true },
            { true, true, true },
        });

        using var response = await PostJsonAsync(client, """{ "cells": [[0,1,0],[0,0,1],[1,1,1]] }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.EndsWith($"/api/v1/boards/{id}", response.Headers.Location?.OriginalString);
        var repository = factory.Services.GetRequiredService<IBoardRepository>();
        Assert.Equal(glider, await repository.FindAsync(id, CancellationToken.None));
    }

    [Theory]
    [InlineData("{}", "cells")]
    [InlineData("""{ "cells": null }""", "cells")]
    [InlineData("""{ "cells": [] }""", "cells")]
    [InlineData("""{ "cells": [[]] }""", "cells[0]")]
    [InlineData("""{ "cells": [null] }""", "cells[0]")]
    [InlineData("""{ "cells": [[0,1],null] }""", "cells[1]")]
    [InlineData("""{ "cells": [[0,1,0],[0,1]] }""", "cells[1]")]
    [InlineData("""{ "cells": [[0,1],[0,1,0]] }""", "cells[1]")]
    [InlineData("""{ "cells": [[2,0],[0,0]] }""", "cells[0][0]")]
    [InlineData("""{ "cells": [[0,1,0],[0,2,0]] }""", "cells[1][1]")]
    [InlineData("""{ "cells": [[0,1,0],[0,1,-1]] }""", "cells[1][2]")]
    public async Task InvalidCellsReturnAValidationProblemUnderTheirFieldPath(string json, string expectedKey)
    {
        using var client = factory.CreateClient();

        using var response = await PostJsonAsync(client, json);

        await AssertValidationProblemAsync(response, expectedKey);
    }

    // Each grid has two problems. The first row's length is checked before the other rows' lengths, row
    // lengths before cell values, and cell values row by row.
    [Theory]
    [InlineData("""{ "cells": [[],[0]] }""", "cells[0]")]
    [InlineData("""{ "cells": [[0,2,0],[0,1]] }""", "cells[1]")]
    [InlineData("""{ "cells": [[0,2],[3,0]] }""", "cells[0][1]")]
    public async Task OnlyTheFirstProblemInCheckOrderIsReported(string json, string expectedKey)
    {
        using var client = factory.CreateClient();

        using var response = await PostJsonAsync(client, json);

        await AssertValidationProblemAsync(response, expectedKey);
    }

    [Fact]
    public async Task TooManyRowsIsReportedBeforeANullRow()
    {
        using var limitedFactory = WithLoweredLimits();
        using var client = limitedFactory.CreateClient();

        // LoweredMaxRows + 1 rows, and the last one is null.
        using var response = await PostJsonAsync(client, """{ "cells": [[0],[0],[0],null] }""");

        await AssertValidationProblemAsync(response, "cells");
    }

    [Theory]
    [InlineData(LoweredMaxRows + 1, LoweredMaxColumns, "cells")]
    [InlineData(LoweredMaxRows, LoweredMaxColumns + 1, "cells[0]")]
    public async Task OneRowOrColumnOverTheConfiguredLimitReturnsAValidationProblem(int rows, int columns, string expectedKey)
    {
        using var limitedFactory = WithLoweredLimits();
        using var client = limitedFactory.CreateClient();

        using var response = await PostJsonAsync(client, DeadGridJson(rows, columns));

        await AssertValidationProblemAsync(response, expectedKey);
    }

    [Fact]
    public async Task BoardExactlyAtTheConfiguredLimitsIsCreated()
    {
        using var limitedFactory = WithLoweredLimits();
        using var client = limitedFactory.CreateClient();

        using var response = await PostJsonAsync(client, DeadGridJson(LoweredMaxRows, LoweredMaxColumns));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ASP.NET Core rejects these bodies itself, before the endpoint runs.
    [Theory]
    [InlineData("""{ "cells": [[0,1,0],[0,1,0]""")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "cells": [["1"]] }""")]
    [InlineData("""{ "cells": [[true]] }""")]
    [InlineData("""{ "cells": [[1.5]] }""")]
    public async Task MalformedOrMistypedBodyReturnsBadRequestProblemDetails(string json)
    {
        using var client = factory.CreateClient();

        using var response = await PostJsonAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("Bad Request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        // The parser's error, such as "Path: $.cells[2] | LineNumber: 0 | BytePositionInLine: 27.", stays out of the response.
        Assert.DoesNotContain("JsonException", body);
        Assert.DoesNotContain("LineNumber", body);
    }

    private WebApplicationFactory<Program> WithLoweredLimits() => factory.WithSettings(
        ("GameOfLife:MaxRows", $"{LoweredMaxRows}"),
        ("GameOfLife:MaxColumns", $"{LoweredMaxColumns}"));

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/api/v1/boards", content);
    }

    // The JSON of an upload whose grid has the given size and only dead cells.
    private static string DeadGridJson(int rows, int columns)
    {
        string row = "[" + string.Join(",", Enumerable.Repeat("0", columns)) + "]";
        return "{ \"cells\": [" + string.Join(",", Enumerable.Repeat(row, rows)) + "] }";
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string expectedKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(expectedKey, error.Name);
    }
}