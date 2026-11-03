using System.Text.Json;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Authentication;
using Namerre.Testing;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Authentication;

public sealed class PasskeyCeremoniesTests : IDisposable
{
    private static readonly PasskeyRelyingParty RelyingParty = new("namerre.example", "https://namerre.example");
    private static readonly byte[] UserHandle = [1, 2, 3, 4, 5, 6, 7, 8];
    private readonly PasskeyCeremonies ceremonies = new();
    private readonly SoftwareAuthenticator authenticator = new(RelyingParty.Origin);

    [Fact]
    public async Task RegistersAndSignsInWithADiscoverableVerifiedPasskey()
    {
        var registration = ceremonies.BeginRegistration(RelyingParty, User([]), UserHandle);
        var options = Json(registration.OptionsJson);
        Assert.Equal("namerre.example", options.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
        Assert.Equal("required",
            options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        Assert.Equal(UserHandle, SoftwareAuthenticator.Base64Url(options.GetProperty("user").GetProperty("id").GetString()!));

        var passkey = await ceremonies.CompleteRegistration(registration.State, authenticator.Create(options),
            CancellationToken.None);
        Assert.Equal(authenticator.CredentialId, passkey.CredentialId);
        Assert.Equal(UserHandle, passkey.UserHandle);

        var stored = Stored(passkey);
        var signIn = ceremonies.BeginAssertion(RelyingParty);
        var answer = authenticator.Get(Json(signIn.OptionsJson));
        Assert.Equal(authenticator.CredentialId, ceremonies.AssertedCredentialId(answer));
        Assert.Equal(1u, await ceremonies.CompleteAssertion(signIn.State, answer, stored, CancellationToken.None));
    }

    [Fact]
    public void RegistrationExcludesTheUsersOtherPasskeys()
    {
        var existing = Stored(new NewPasskey([9, 9, 9], [1], UserHandle, 0));
        var options = Json(ceremonies.BeginRegistration(RelyingParty, User([existing]), UserHandle).OptionsJson);

        var excluded = Assert.Single(options.GetProperty("excludeCredentials").EnumerateArray());
        Assert.Equal([9, 9, 9], SoftwareAuthenticator.Base64Url(excluded.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task AnswersFromAnotherOriginAreRejected()
    {
        var registration = ceremonies.BeginRegistration(RelyingParty, User([]), UserHandle);
        var options = Json(registration.OptionsJson);
        authenticator.Origin = "https://phishing.example";

        await Assert.ThrowsAsync<PasskeyVerificationException>(() =>
            ceremonies.CompleteRegistration(registration.State, authenticator.Create(options), CancellationToken.None));
    }

    [Fact]
    public async Task UnverifiedUsersAreRejected()
    {
        var stored = await Register();
        authenticator.Verifies = false;
        var signIn = ceremonies.BeginAssertion(RelyingParty);

        await Assert.ThrowsAsync<PasskeyVerificationException>(() => ceremonies.CompleteAssertion(signIn.State,
            authenticator.Get(Json(signIn.OptionsJson)), stored, CancellationToken.None));
    }

    [Fact]
    public async Task AnswersToAnotherChallengeAreRejected()
    {
        var stored = await Register();
        var first = ceremonies.BeginAssertion(RelyingParty);
        var second = ceremonies.BeginAssertion(RelyingParty);

        await Assert.ThrowsAsync<PasskeyVerificationException>(() => ceremonies.CompleteAssertion(second.State,
            authenticator.Get(Json(first.OptionsJson)), stored, CancellationToken.None));
    }

    [Fact]
    public async Task AWrongKeyOrUserHandleIsRejected()
    {
        var stored = await Register();
        var signIn = ceremonies.BeginAssertion(RelyingParty);
        var answer = authenticator.Get(Json(signIn.OptionsJson));

        await Assert.ThrowsAsync<PasskeyVerificationException>(() => ceremonies.CompleteAssertion(signIn.State,
            answer, stored with { UserHandle = [7, 7, 7] }, CancellationToken.None));
        using var other = new SoftwareAuthenticator(RelyingParty.Origin);
        var registration = ceremonies.BeginRegistration(RelyingParty, User([]), UserHandle);
        var otherKey = await ceremonies.CompleteRegistration(registration.State,
            other.Create(Json(registration.OptionsJson)), CancellationToken.None);
        await Assert.ThrowsAsync<PasskeyVerificationException>(() => ceremonies.CompleteAssertion(signIn.State,
            answer, stored with { PublicKey = otherKey.PublicKey }, CancellationToken.None));
    }

    [Fact]
    public async Task AGoingBackSignatureCounterIsRejectedAsAClone()
    {
        var stored = await Register();
        var signIn = ceremonies.BeginAssertion(RelyingParty);
        authenticator.Counter = 4;

        await Assert.ThrowsAsync<PasskeyVerificationException>(() => ceremonies.CompleteAssertion(signIn.State,
            authenticator.Get(Json(signIn.OptionsJson)), stored with { SignCount = 10 }, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"id\":\"AAAA\",\"rawId\":\"%%%\",\"type\":\"public-key\"}")]
    public async Task GarbageIsAVerificationFailure(string garbage)
    {
        var stored = await Register();
        var signIn = ceremonies.BeginAssertion(RelyingParty);

        Assert.Null(ceremonies.AssertedCredentialId(garbage));
        await Assert.ThrowsAsync<PasskeyVerificationException>(() =>
            ceremonies.CompleteAssertion(signIn.State, garbage, stored, CancellationToken.None));
        await Assert.ThrowsAsync<PasskeyVerificationException>(() =>
            ceremonies.CompleteAssertion(garbage, authenticator.Get(Json(signIn.OptionsJson)), stored,
                CancellationToken.None));
    }

    public void Dispose() => authenticator.Dispose();

    private async Task<UserPasskeyRecord> Register()
    {
        var registration = ceremonies.BeginRegistration(RelyingParty, User([]), UserHandle);
        return Stored(await ceremonies.CompleteRegistration(registration.State,
            authenticator.Create(Json(registration.OptionsJson)), CancellationToken.None));
    }

    private static UserPasskeyRecord Stored(NewPasskey passkey) => new(1, 1, passkey.CredentialId, passkey.PublicKey,
        passkey.UserHandle, passkey.SignCount, "Laptop", DateTimeOffset.UnixEpoch, null);

    private static UserRecord User(IReadOnlyList<UserPasskeyRecord> passkeys) => new(1, "alice", null, UserRole.User,
        false, "stamp", DateTimeOffset.UnixEpoch, null, [], passkeys);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
