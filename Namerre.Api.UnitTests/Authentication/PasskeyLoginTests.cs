using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Authentication;
using Namerre.Infrastructure.Configuration;
using Xunit;

namespace Namerre.Api.UnitTests.Authentication;

public sealed class PasskeyLoginTests
{
    private static readonly UserRecord Alice = new(7, "alice", null, UserRole.User, false, "stamp-1",
        DateTimeOffset.UnixEpoch, null, [], []);

    [Fact]
    public void TheRelyingPartyIsTheRequestsOwnOriginByDefault()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("media.example.org:8443");

        Assert.Equal(new PasskeyRelyingParty("media.example.org", "https://media.example.org:8443"),
            Login().RelyingParty(context.Request));
    }

    [Theory]
    [InlineData("https://namerre.example.com", "namerre.example.com", "https://namerre.example.com")]
    [InlineData("https://namerre.example.com:8443/", "namerre.example.com", "https://namerre.example.com:8443")]
    [InlineData("http://localhost:5251", "localhost", "http://localhost:5251")]
    public void AConfiguredOriginWins(string origin, string id, string expectedOrigin)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("internal:8080");

        Assert.Equal(new PasskeyRelyingParty(id, expectedOrigin), Login(origin).RelyingParty(context.Request));
    }

    [Theory]
    [InlineData("namerre.example.com")]
    [InlineData("http://namerre.example.com")]
    [InlineData("https://namerre.example.com/app")]
    [InlineData("https://192.0.2.1")]
    [InlineData("https://user@namerre.example.com")]
    public void InvalidOriginsFailStartup(string origin)
        => Assert.Contains("Passkeys:Origin", Assert.Throws<InvalidOperationException>(() => Login(origin)).Message);

    [Fact]
    public void TicketsWorkOnceForTheirPurpose()
    {
        var login = Login();
        var ticket = login.Issue(PasskeyLogin.RegisterPurpose, "state", Alice);

        Assert.Null(login.Redeem(ticket, PasskeyLogin.SignInPurpose));
        Assert.Equal(("state", 7L, "stamp-1"), login.Redeem(ticket, PasskeyLogin.RegisterPurpose));
        Assert.Null(login.Redeem(ticket, PasskeyLogin.RegisterPurpose));
        Assert.Null(login.Redeem(ticket[..^2], PasskeyLogin.RegisterPurpose));
        Assert.Null(login.Redeem(null, PasskeyLogin.RegisterPurpose));

        var signIn = login.Issue(PasskeyLogin.SignInPurpose, "challenge");
        Assert.Equal(("challenge", (long?)null, (string?)null), login.Redeem(signIn, PasskeyLogin.SignInPurpose));
    }

    [Fact]
    public void TicketsDoNotSurviveARestart()
    {
        // The same Data Protection keys, as after a restart: the used-ticket set is gone, so the ticket must be too.
        var keys = new EphemeralDataProtectionProvider();
        var ticket = Login(keys: keys).Issue(PasskeyLogin.SignInPurpose, "state");

        Assert.Null(Login(keys: keys).Redeem(ticket, PasskeyLogin.SignInPurpose));
    }

    [Fact]
    public void TicketsOfAnotherInstallationAreRejected()
        => Assert.Null(Login().Redeem(Login().Issue(PasskeyLogin.SignInPurpose, "state"), PasskeyLogin.SignInPurpose));

    private static PasskeyLogin Login(string? origin = null, IDataProtectionProvider? keys = null) => new(
        Options.Create(new NamerreOptions
        {
            Authentication = new AuthenticationOptions { Passkeys = new PasskeyOptions { Origin = origin } }
        }), keys ?? new EphemeralDataProtectionProvider());
}
