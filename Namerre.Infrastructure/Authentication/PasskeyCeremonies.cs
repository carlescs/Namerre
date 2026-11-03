using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Namerre.Application.Authentication;

namespace Namerre.Infrastructure.Authentication;

/// <summary>
/// WebAuthn ceremonies with Fido2NetLib. Passkeys are discoverable credentials that require user verification;
/// attestation is not requested, so any authenticator the browser offers is accepted. The state carries the
/// original options and the expected origin, and the caller keeps it from being tampered with.
/// </summary>
public sealed class PasskeyCeremonies : IPasskeyCeremonies
{
    // Matches the browser timeout to the five minutes the host keeps a ceremony open.
    private const uint TimeoutMilliseconds = 300_000;

    public PasskeyChallenge BeginRegistration(PasskeyRelyingParty relyingParty, UserRecord user, byte[] userHandle)
    {
        var options = Create(relyingParty).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = userHandle, Name = user.Username, DisplayName = user.Username },
            ExcludeCredentials = user.Passkeys.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId)).ToArray(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
        options.Timeout = TimeoutMilliseconds;
        var json = options.ToJson();
        return new PasskeyChallenge(json, State(relyingParty, json));
    }

    public async Task<NewPasskey> CompleteRegistration(string state, string responseJson,
        CancellationToken cancellationToken)
    {
        var (relyingParty, optionsJson) = ReadState(state);
        try
        {
            var options = CredentialCreateOptions.FromJson(optionsJson);
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(responseJson)
                ?? throw new PasskeyVerificationException("The registration answer is empty.");
            var credential = await Create(relyingParty).MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                // Uniqueness across users is checked when the passkey is stored.
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
            }, cancellationToken);
            return new NewPasskey(credential.Id, credential.PublicKey, credential.User.Id, credential.SignCount);
        }
        catch (Exception exception) when (IsVerificationFailure(exception))
        {
            throw new PasskeyVerificationException(exception.Message, exception);
        }
    }

    public PasskeyChallenge BeginAssertion(PasskeyRelyingParty relyingParty)
    {
        var options = Create(relyingParty).GetAssertionOptions(new GetAssertionOptionsParams
        {
            // Empty: the browser offers every discoverable passkey of this relying party.
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required
        });
        options.Timeout = TimeoutMilliseconds;
        var json = options.ToJson();
        return new PasskeyChallenge(json, State(relyingParty, json));
    }

    public byte[]? AssertedCredentialId(string responseJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson)?.RawId is
                { Length: > 0 } id ? id : null;
        }
        catch (Exception exception) when (IsVerificationFailure(exception))
        {
            return null;
        }
    }

    public async Task<uint> CompleteAssertion(string state, string responseJson, UserPasskeyRecord passkey,
        CancellationToken cancellationToken)
    {
        var (relyingParty, optionsJson) = ReadState(state);
        try
        {
            var options = AssertionOptions.FromJson(optionsJson);
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson)
                ?? throw new PasskeyVerificationException("The sign-in answer is empty.");
            var result = await Create(relyingParty).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = passkey.SignCount,
                // The answer must name the user the passkey was created for.
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                    args.CredentialId.AsSpan().SequenceEqual(passkey.CredentialId) &&
                    args.UserHandle.AsSpan().SequenceEqual(passkey.UserHandle))
            }, cancellationToken);
            return result.SignCount;
        }
        catch (Exception exception) when (IsVerificationFailure(exception))
        {
            throw new PasskeyVerificationException(exception.Message, exception);
        }
    }

    private static Fido2 Create(PasskeyRelyingParty relyingParty) => new(new Fido2Configuration
    {
        RPID = relyingParty.Id,
        RPName = PasskeyRelyingParty.Name,
        Origins = new HashSet<string>(StringComparer.Ordinal) { relyingParty.Origin }
    });

    private static string State(PasskeyRelyingParty relyingParty, string optionsJson)
        => JsonSerializer.Serialize(new CeremonyState(relyingParty.Id, relyingParty.Origin, optionsJson));

    private static (PasskeyRelyingParty RelyingParty, string OptionsJson) ReadState(string state)
    {
        try
        {
            var read = JsonSerializer.Deserialize<CeremonyState>(state);
            if (read is { RelyingPartyId.Length: > 0, Origin.Length: > 0, Options.Length: > 0 })
                return (new PasskeyRelyingParty(read.RelyingPartyId, read.Origin), read.Options);
        }
        catch (JsonException)
        {
        }
        throw new PasskeyVerificationException("The ceremony state is invalid.");
    }

    private static bool IsVerificationFailure(Exception exception)
        => exception is Fido2VerificationException or JsonException or FormatException or ArgumentException or
            InvalidOperationException or NotSupportedException or System.Formats.Cbor.CborContentException or
            System.Security.Cryptography.CryptographicException;

    private sealed record CeremonyState(string RelyingPartyId, string Origin, string Options);
}
