namespace GameOfLife.Core.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void FingerprintHashesCompactUtf8JsonOfTheMatrix()
    {
        var board = Board.FromMatrix([[1, 0, 1], [0, 1, 1]]);

        // Independent SHA-256 test vector for UTF-8 JSON [[1,0,1],[0,1,1]].
        Assert.Equal("11F7A18C9FA9D32EDB80BC5E8D04AA0A48555BC63A991A578EDD459C36C63376", board.Fingerprint());
        Assert.Equal(board.Fingerprint(), Board.FromMatrix(board.ToMatrix()).Fingerprint());
    }

    [Theory]
    [InlineData("#.#/.##", "#.#/.#.")]
    [InlineData("#.#/.##", "#./#./##")]
    [InlineData("#..", "..#")]
    [InlineData("..", "./.")]
    public void ShapeCellValueAndCellOrderAffectTheFingerprint(string pattern, string other)
    {
        Assert.NotEqual(Board.FromMatrix(Pattern.Parse(pattern)).Fingerprint(),
            Board.FromMatrix(Pattern.Parse(other)).Fingerprint());
    }
}