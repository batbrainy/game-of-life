namespace GameOfLife.Core.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void FingerprintUsesBigEndianDimensionsAndRowMajorCellBytes()
    {
        var board = Board.FromMatrix([[1, 0, 1], [0, 1, 1]]);

        // Independent SHA-256 test vector for bytes 00000002 00000003 010001000101.
        Assert.Equal("2667A128D34FE2527845A727D109C81EE4EC2B197F2CF5B146C158A0584DF075", board.Fingerprint());
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