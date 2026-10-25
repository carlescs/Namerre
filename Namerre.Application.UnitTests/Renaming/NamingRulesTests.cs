using Namerre.Domain.Renaming;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class NamingRulesTests
{
    [Fact]
    public void LiteralRejectsEmptySearch()
        => Assert.Throws<ArgumentException>(() => NamingRules.Literal("name", "", "x", false, "all"));

    [Theory]
    [InlineData("")]
    [InlineData("last")]
    [InlineData("ALL")]
    public void LiteralRejectsUnknownScope(string scope)
        => Assert.Throws<ArgumentException>(() => NamingRules.Literal("name", "a", "x", false, scope));

    [Fact]
    public void LiteralFirstScopeLeavesStemWithoutMatchUnchanged()
        => Assert.Equal("name", NamingRules.Literal("name", "zz", "x", false, "first"));

    [Fact]
    public void LiteralFirstScopeIgnoringCaseReplacesOnlyTheEarliestMatch()
        => Assert.Equal("x-A-a", NamingRules.Literal("a-A-a", "A", "x", true, "first"));

    [Theory]
    [InlineData(false, "-A-")]
    [InlineData(true, "---")]
    public void RegexHonoursIgnoreCase(bool ignoreCase, string expected)
        => Assert.Equal(expected, NamingRules.RegexReplace("aAa", "a", "-", ignoreCase));

    [Fact]
    public void RegexRejectsInvalidPatterns()
        => Assert.ThrowsAny<ArgumentException>(() => NamingRules.RegexReplace("name", "(", "", false));

    [Theory]
    [InlineData("upper", "THE QUICK FOX")]
    [InlineData("lower", "the quick fox")]
    [InlineData("title", "The Quick Fox")]
    public void ChangeCaseSupportsEachMode(string mode, string expected)
        => Assert.Equal(expected, NamingRules.ChangeCase("tHe QUICK fox", mode));

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("camel")]
    public void ChangeCaseRejectsUnknownModes(string mode)
        => Assert.Throws<ArgumentException>(() => NamingRules.ChangeCase("name", mode));

    [Theory]
    [InlineData("file.txt")]
    [InlineData("Movies/Alien (1979)/Alien.mkv")]
    [InlineData("Música/Ñandú.flac")]
    [InlineData("emoji/😀.png")]
    [InlineData("CONSOLE.txt")]
    [InlineData("folder/.hidden")]
    public void AcceptsValidRelativePaths(string path)
        => NamingRules.ValidateRelativePath(path);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("trailing/")]
    [InlineData("a/b<c")]
    [InlineData("a/b>c")]
    [InlineData("a/b\"c")]
    [InlineData("a/b|c")]
    [InlineData("a/b?c")]
    [InlineData("a/b*c")]
    [InlineData("a/b\tc")]
    [InlineData("nul")]
    [InlineData("com1.txt")]
    [InlineData("LPT9")]
    [InlineData("prn.log")]
    [InlineData(".NAMERRE-journal")]
    public void RejectsEmptyReservedAndInvalidSegments(string path)
        => Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(path));

    [Fact]
    public void RejectsOverlongPathsAndSegments()
    {
        Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(
            string.Join('/', Enumerable.Repeat(new string('a', 200), 11))));
        Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(new string('a', 241)));
        NamingRules.ValidateRelativePath(new string('a', 240));
    }

    [Fact]
    public void RejectsSegmentsOverTheUtf8ByteLimit()
    {
        // 86 three-byte characters fit the character limit but exceed 255 UTF-8 bytes.
        Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(new string('€', 86)));
        NamingRules.ValidateRelativePath(new string('€', 85));
    }

    [Fact]
    public void RejectsUnpairedSurrogates()
    {
        // Built in code: xUnit replaces lone surrogates in serialized InlineData values.
        foreach (var path in new[] { "bad\uD800.txt", "bad\uDC00.txt", "bad\uD800", "\uDC00\uD800" })
            Assert.Throws<ArgumentException>(() => NamingRules.ValidateRelativePath(path));
    }

    [Theory]
    [InlineData("Queued", "Queued", true)]
    [InlineData("Queued", "RecoveryRequired", true)]
    [InlineData("Completed", "RecoveryRequired", true)]
    [InlineData("RecoveryQueued", "RecoveryRequired", true)]
    [InlineData("Recovering", "RecoveryRequired", true)]
    [InlineData("Recovered", "RecoveryRequired", true)]
    [InlineData("Queued", "Completed", false)]
    [InlineData("RecoveryRequired", "Recovered", false)]
    [InlineData("Running", "Queued", false)]
    [InlineData("Unknown", "Running", false)]
    public void RunTransitionsCoverRecoveryEntryPoints(string from, string to, bool allowed)
        => Assert.Equal(allowed, RunTransitions.Allows(from, to));

    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Recovered", true)]
    [InlineData("Queued", false)]
    [InlineData("Running", false)]
    [InlineData("RecoveryRequired", false)]
    [InlineData("RecoveryQueued", false)]
    [InlineData("Recovering", false)]
    public void OnlyCompletedAndRecoveredRunsAreSettled(string status, bool settled)
        => Assert.Equal(settled, RunTransitions.IsSettled(status));
}
