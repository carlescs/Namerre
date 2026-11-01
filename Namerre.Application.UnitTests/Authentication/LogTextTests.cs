using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class LogTextTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("alice\r\nINFO forged entry", "aliceINFO forged entry")]
    [InlineData("a\nb\rc", "abc")]
    [InlineData(null, "")]
    public void LineBreaksAreRemoved(string? value, string expected) => Assert.Equal(expected, LogText.Clean(value));
}
