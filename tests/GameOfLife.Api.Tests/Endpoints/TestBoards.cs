using System.Net;
using System.Text;
using System.Text.Json;

namespace GameOfLife.Api.Tests.Endpoints;

// Boards for the endpoint tests. A pattern is text: rows separated by '/', '#' for a live cell and '.' for a dead
// one, so "#.#/.##" is two rows of three cells.
internal static class TestBoards
{
    // The cells of a rows x columns board as JSON rows of 0 and 1, written the way the API writes them:
    // [[0,1,0],[0,0,1]]. Every cell is dead except the pattern, whose top-left cell is at (top, left).
    public static string CellsJson(int rows, int columns, int top, int left, string pattern)
    {
        var cells = new int[rows][];
        for (int row = 0; row < rows; row++)
        {
            cells[row] = new int[columns];
        }

        string[] patternRows = pattern.Split('/');
        for (int row = 0; row < patternRows.Length; row++)
        {
            for (int column = 0; column < patternRows[row].Length; column++)
            {
                cells[top + row][left + column] = patternRows[row][column] switch
                {
                    '#' => 1,
                    '.' => 0,
                    _ => throw new ArgumentException($"\"{pattern}\" contains '{patternRows[row][column]}'.", nameof(pattern)),
                };
            }
        }

        return JsonSerializer.Serialize(cells);
    }

    // Uploads the cells and returns the id of the new board. Fails the test unless the upload returns 201.
    public static async Task<Guid> UploadAsync(HttpClient client, string cellsJson)
    {
        using var content = new StringContent($$"""{ "cells": {{cellsJson}} }""", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/boards", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}