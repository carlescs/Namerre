using Namerre.Infrastructure.Authentication;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Authentication;

public sealed class PasswordHasherTests
{
    private const string Password = "correct horse battery"; // betterleaks:allow
    private static readonly PasswordHasher Hasher = new();
    // Hashing is deliberately slow, so share one hash across tests.
    private static readonly string Hash = Hasher.Hash(Password);

    [Fact]
    public void HashesVerifyOnlyTheirOwnPassword()
    {
        Assert.True(Hasher.IsValidHash(Hash));
        Assert.True(Hasher.Verify(Hash, Password));
        Assert.False(Hasher.Verify(Hash, "wrong password"));
        // Users without a password never verify, after the same work as a wrong password.
        Assert.False(Hasher.Verify(null, Password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AQIDBA==")]
    public void MalformedHashesAreInvalid(string? hash) => Assert.False(Hasher.IsValidHash(hash));

    [Fact]
    public void HashesWithAWeakerWorkFactorAreInvalid()
    {
        var bytes = Convert.FromBase64String(Hash);
        bytes[5] = 0; bytes[6] = 0; bytes[7] = 0x27; bytes[8] = 0x10; // 10,000 iterations

        Assert.False(Hasher.IsValidHash(Convert.ToBase64String(bytes)));
    }
}
