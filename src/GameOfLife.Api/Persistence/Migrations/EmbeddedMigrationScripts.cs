using System.Globalization;
using System.Text.RegularExpressions;

namespace GameOfLife.Api.Persistence.Migrations;

/// <summary>The migration scripts in Persistence/Migrations/Scripts, embedded in this assembly when it is built.</summary>
public static partial class EmbeddedMigrationScripts
{
    /// <summary>Returns every embedded script, in version order.</summary>
    /// <exception cref="FormatException">A script's file name is not NNNN_lower_snake_case.sql.</exception>
    public static IReadOnlyList<MigrationScript> Load()
    {
        var assembly = typeof(EmbeddedMigrationScripts).Assembly;
        var scripts = new List<MigrationScript>();

        // The project file gives each embedded script its file name as its resource name.
        foreach (string fileName in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)))
        {
            var (version, name) = ParseFileName(fileName);
            using var stream = assembly.GetManifestResourceStream(fileName)
                ?? throw new InvalidOperationException($"The embedded script '{fileName}' could not be opened.");
            using var reader = new StreamReader(stream);
            scripts.Add(new MigrationScript(version, name, reader.ReadToEnd()));
        }

        return scripts.OrderBy(script => script.Version).ToList();
    }

    /// <summary>Reads the version and name from a file name such as <c>0001_create_boards.sql</c>.</summary>
    /// <exception cref="FormatException">The file name is not NNNN_lower_snake_case.sql.</exception>
    public static (int Version, string Name) ParseFileName(string fileName)
    {
        var match = FileNamePattern().Match(fileName);
        if (!match.Success)
        {
            throw new FormatException(
                $"Migration script '{fileName}' must be named NNNN_lower_snake_case.sql, for example 0001_create_boards.sql.");
        }

        return (int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture), match.Groups["name"].Value);
    }

    // Four digits, an underscore, then words of lowercase letters and digits joined by single underscores.
    [GeneratedRegex(@"^(?<version>[0-9]{4})_(?<name>[a-z0-9]+(_[a-z0-9]+)*)\.sql$")]
    private static partial Regex FileNamePattern();
}