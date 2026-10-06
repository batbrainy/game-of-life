using System.Diagnostics.CodeAnalysis;

using GameOfLife.Api.Configuration;

namespace GameOfLife.Api.Endpoints;

/// <summary>Checks that an uploaded grid is present, within the configured size limits, rectangular, and made of 0s and 1s.</summary>
public static class BoardRequestValidator
{
    /// <summary>
    /// Checks <paramref name="cells"/> and stops at the first problem. <paramref name="errors"/> then holds that one
    /// problem under its field path: <c>cells</c>, <c>cells[2]</c> or <c>cells[1][4]</c>.
    /// </summary>
    /// <returns><see langword="true"/> when the grid is valid; <paramref name="errors"/> is then empty.</returns>
    public static bool TryValidate([NotNullWhen(true)] int[][]? cells, GameOfLifeOptions limits, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (cells is null)
        {
            errors["cells"] = ["The cells field is required; it is missing or null."];
            return false;
        }

        if (cells.Length < 1 || cells.Length > limits.MaxRows)
        {
            errors["cells"] = [$"A board must have 1 to {limits.MaxRows} rows; this one has {cells.Length}."];
            return false;
        }

        // System.Text.Json does not enforce the non-nullable row type: [[0,1],null] arrives with a null row.
        for (int row = 0; row < cells.Length; row++)
        {
            if (cells[row] is null)
            {
                errors[$"cells[{row}]"] = ["A row must be an array of cells; this one is null."];
                return false;
            }
        }

        int columns = cells[0].Length;
        if (columns < 1 || columns > limits.MaxColumns)
        {
            errors["cells[0]"] = [$"A row must have 1 to {limits.MaxColumns} cells; this one has {columns}."];
            return false;
        }

        for (int row = 1; row < cells.Length; row++)
        {
            if (cells[row].Length != columns)
            {
                errors[$"cells[{row}]"] = [$"Every row must have as many cells as the first row, which has {columns}; this one has {cells[row].Length}."];
                return false;
            }
        }

        for (int row = 0; row < cells.Length; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int value = cells[row][column];
                if (value != 0 && value != 1)
                {
                    errors[$"cells[{row}][{column}]"] = [$"A cell must be 0 or 1; this one is {value}."];
                    return false;
                }
            }
        }

        return true;
    }
}