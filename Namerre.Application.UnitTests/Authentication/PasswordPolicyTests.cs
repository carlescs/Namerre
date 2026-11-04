using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("correct horse battery")] // betterleaks:allow
    [InlineData("alice-new-password")] // betterleaks:allow
    [InlineData("Tr0ub4dor&3-staple")] // betterleaks:allow
    public void LongUncommonPasswordsAreAccepted(string password)
        => Assert.True(PasswordPolicy.IsAcceptable(password, "alice"));

    [Theory]
    [InlineData("short")]
    [InlineData("            ")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abababababab")]
    [InlineData("123412341234")]
    public void ShortBlankAndRepetitivePasswordsAreRefused(string password)
        => Assert.False(PasswordPolicy.IsAcceptable(password));

    [Theory]
    [InlineData("qwerty123456")]
    [InlineData("1q2w3e4r5t6y")]
    [InlineData("QWERTY123456")]
    [InlineData("Password2024!!")]
    [InlineData("!!Football1234")]
    [InlineData("  iloveyou12345  ")]
    public void CommonAndBreachedPasswordsAreRefusedIgnoringCaseAndSurroundingDigits(string password)
        => Assert.False(PasswordPolicy.IsAcceptable(password));

    [Theory]
    [InlineData("carlescs2024!")]
    [InlineData("CARLESCScarlescs")]
    public void PasswordsMadeMostlyOfTheUsernameAreRefused(string password)
    {
        Assert.False(PasswordPolicy.IsAcceptable(password, "carlescs"));
        Assert.True(PasswordPolicy.IsAcceptable(password, "someone"));
    }

    [Fact]
    public void TheBundledListIsLoaded()
        => Assert.True(CommonPasswords.Count > 20_000);
}
