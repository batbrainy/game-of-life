using GameOfLife.Api.Persistence.Migrations;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class BoardsTableTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EmbeddedScriptsCreateBoardsWithExactlyFiveColumns()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();

        // udt_name is PostgreSQL's own name for a type: int4 is integer and _bool is boolean[].
        await using var command = dataSource.CreateCommand(
            "SELECT column_name, udt_name, is_nullable FROM information_schema.columns WHERE table_name = 'boards' ORDER BY ordinal_position");
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<(string Name, string Type, string IsNullable)>();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        Assert.Equal(
            new[]
            {
                ("id", "uuid", "NO"),
                ("row_count", "int4", "NO"),
                ("column_count", "int4", "NO"),
                ("cells", "_bool", "NO"),
                ("created_at", "timestamptz", "NO"),
            },
            columns);
    }

    [Fact]
    public async Task ValidRowInserts()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        bool[] cells = [false, true, false, false, true, false, false, true, false];

        await InsertAsync(dataSource, 3, 3, cells);

        await using var count = dataSource.CreateCommand("SELECT count(*) FROM boards");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(0, 3, 0, "boards_row_count_check")]
    [InlineData(3, 0, 0, "boards_column_count_check")]
    [InlineData(3, 3, 10, "boards_cells_shape")]
    [InlineData(3, 3, 8, "boards_cells_shape")]
    public async Task ZeroDimensionOrOneCellTooManyOrTooFewIsRejectedByACheck(int rowCount, int columnCount, int cellCount, string constraint)
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();

        await AssertRejectedByCheckAsync(constraint, () => InsertAsync(dataSource, rowCount, columnCount, new bool[cellCount]));
    }

    [Fact]
    public async Task CellsContainingANullAreRejectedByTheShapeCheck()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        bool?[] cells = [false, true, null, false, true, false, false, true, false];

        await AssertRejectedByCheckAsync("boards_cells_shape", () => InsertAsync(dataSource, 3, 3, cells));
    }

    [Fact]
    public async Task TwoDimensionalCellsAreRejectedByTheShapeCheck()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();

        await AssertRejectedByCheckAsync("boards_cells_shape", () => InsertAsync(dataSource, 3, 3, new bool[3, 3]));
    }

    private async Task<NpgsqlDataSource> CreateMigratedDatabaseAsync()
    {
        var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await new MigrationRunner(dataSource, NullLogger<MigrationRunner>.Instance)
            .ApplyAsync(EmbeddedMigrationScripts.Load(), CancellationToken.None);
        return dataSource;
    }

    private static async Task InsertAsync(NpgsqlDataSource dataSource, int rowCount, int columnCount, object cells)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO boards (id, row_count, column_count, cells) VALUES ($1, $2, $3, $4)");
        command.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter { Value = rowCount });
        command.Parameters.Add(new NpgsqlParameter { Value = columnCount });
        command.Parameters.Add(new NpgsqlParameter { Value = cells });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertRejectedByCheckAsync(string constraint, Func<Task> insert)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(insert);

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }
}