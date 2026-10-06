using GameOfLife.Api.Persistence;
using GameOfLife.Api.Persistence.Migrations;
using GameOfLife.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class NpgsqlBoardRepositoryTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 7)]
    [InlineData(256, 256)]
    public async Task AddedBoardIsFoundEqualToTheOriginal(int rows, int columns)
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        var repository = new NpgsqlBoardRepository(dataSource);
        var board = RandomBoard(rows, columns);
        var id = Guid.NewGuid();

        await repository.AddAsync(id, board, CancellationToken.None);
        var found = await repository.FindAsync(id, CancellationToken.None);

        Assert.Equal(board, found);
    }

    [Fact]
    public async Task UnknownIdIsNotFound()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        var repository = new NpgsqlBoardRepository(dataSource);
        await repository.AddAsync(Guid.NewGuid(), RandomBoard(3, 3), CancellationToken.None);

        Assert.Null(await repository.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StoredCellsReadWithRawSqlEqualTheCellArray()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        var board = RandomBoard(5, 7);
        var id = Guid.NewGuid();

        await new NpgsqlBoardRepository(dataSource).AddAsync(id, board, CancellationToken.None);

        await using var command = dataSource.CreateCommand("SELECT cells FROM boards WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        Assert.Equal(board.ToCellArray(), Assert.IsType<bool[]>(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task AddingTheSameIdTwiceIsAUniqueViolation()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        var repository = new NpgsqlBoardRepository(dataSource);
        var id = Guid.NewGuid();
        await repository.AddAsync(id, RandomBoard(3, 3), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => repository.AddAsync(id, RandomBoard(5, 7), CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task AlreadyCancelledTokenMakesBothMethodsThrowAndStoresNothing()
    {
        await using var dataSource = await CreateMigratedDatabaseAsync();
        var repository = new NpgsqlBoardRepository(dataSource);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.AddAsync(Guid.NewGuid(), RandomBoard(3, 3), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.FindAsync(Guid.NewGuid(), cancellation.Token));

        await using var count = dataSource.CreateCommand("SELECT count(*) FROM boards");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    private async Task<NpgsqlDataSource> CreateMigratedDatabaseAsync()
    {
        var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await new MigrationRunner(dataSource, NullLogger<MigrationRunner>.Instance)
            .ApplyAsync(EmbeddedMigrationScripts.Load(), CancellationToken.None);
        return dataSource;
    }

    private static Board RandomBoard(int rows, int columns)
    {
        // A fixed seed gives the same "random" cells on every run.
        var random = new Random(42);
        bool[] cells = new bool[rows * columns];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = random.Next(2) == 1;
        }

        return Board.FromCellArray(rows, columns, cells);
    }
}