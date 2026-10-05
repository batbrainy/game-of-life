namespace GameOfLife.Api.Persistence.Migrations;

public sealed record MigrationScript(int Version, string Name, string Sql);