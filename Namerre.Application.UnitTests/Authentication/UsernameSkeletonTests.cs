using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class UsernameSkeletonTests
{
    [Theory]
    [InlineData("admin", "\u0430dmin")]
    [InlineData("admin", "ADMIN")]
    [InlineData("admin", "adm1n")]
    [InlineData("admin", "adm\u0456n")]
    [InlineData("scope", "s\u0441\u043Epe")]
    [InlineData("pony", "\u0440\u043Eny")]
    [InlineData("jo0", "Jo\u039F")]
    [InlineData("cafe", "caf\u00E9")]
    public void LookAlikesShareASkeleton(string a, string b)
        => Assert.Equal(UsernameSkeleton.Of(a), UsernameSkeleton.Of(b));

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("alice", "alicia")]
    [InlineData("user", "users")]
    public void DifferentNamesHaveDifferentSkeletons(string a, string b)
        => Assert.NotEqual(UsernameSkeleton.Of(a), UsernameSkeleton.Of(b));

    [Fact]
    public void SkeletonIgnoresInvisibleCharacters()
        => Assert.Equal(UsernameSkeleton.Of("admin"), UsernameSkeleton.Of("ad\u200Bm\u202Ein\uFEFF"));

    [Fact]
    public void SkeletonOfAMalformedStoredNameDoesNotThrow()
        => Assert.Equal("a\uD800b", UsernameSkeleton.Of("A\uD800b"));

    [Theory]
    [InlineData("  alice  ", "alice")]
    [InlineData("\uFF21\uFF22", "AB")]
    [InlineData("a b", "a b")]
    [InlineData("e\u0301", "\u00E9")]
    public void CanonicalTrimsAndNormalizes(string input, string expected)
        => Assert.Equal(expected, UsernameSkeleton.Canonical(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    [InlineData("a\u200Bb")]
    [InlineData("a\u202Eb")]
    [InlineData("a\uFEFFb")]
    [InlineData("a\u2028b")]
    [InlineData("a\uE000b")]
    public void CanonicalRejectsUnprintableText(string? input)
        => Assert.Null(UsernameSkeleton.Canonical(input));

    [Fact]
    public void CanonicalRejectsSurrogateHalvesAndNoncharacters()
    {
        // Built at runtime: xUnit would replace these characters when it serializes theory data.
        foreach (var bad in new[] { 0xDC00, 0xD800, 0xFDD0, 0xFFFE, 0xFFFF, 0xE000 })
            Assert.Null(UsernameSkeleton.Canonical("a" + (char)bad + "b"));
    }

    [Fact]
    public void CleanDropsWhatCanonicalWouldRefuse()
        => Assert.Equal("ab c", UsernameSkeleton.Clean(" a\u200Bb\u202E\uD800 c\uFEFF"));
}
