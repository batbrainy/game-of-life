using GameOfLife.Api.Persistence.Migrations;

namespace GameOfLife.Api.Tests.Persistence;

public sealed class EmbeddedMigrationScriptsTests
{
    [Fact]
    public void LoadReturnsTheBoardsTableScript()
    {
        var script = Assert.Single(EmbeddedMigrationScripts.Load());

        Assert.Equal(1, script.Version);
        Assert.Equal("create_boards", script.Name);
        Assert.Contains("CREATE TABLE boards", script.Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0001_create_boards.sql", 1, "create_boards")]
    [InlineData("0012_add_index.sql", 12, "add_index")]
    public void ParseFileNameReadsTheVersionAndName(string fileName, int version, string name)
    {
        Assert.Equal((version, name), EmbeddedMigrationScripts.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("1_x.sql")]
    [InlineData("0001-create.sql")]
    [InlineData("0001_Create.sql")]
    public void ParseFileNameRejectsOtherNamesAndNamesTheFile(string fileName)
    {
        var exception = Assert.Throws<FormatException>(() => EmbeddedMigrationScripts.ParseFileName(fileName));

        Assert.Contains(fileName, exception.Message, StringComparison.Ordinal);
    }
}