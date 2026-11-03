namespace Namerre.Application.Authentication;

/// <summary>
/// The WebAuthn protocol: builds the options of a registration or sign-in and verifies the browser's answer.
/// Verification failures throw <see cref="PasskeyVerificationException"/>.
/// </summary>
public interface IPasskeyCeremonies
{
    /// <summary>Starts registering a discoverable passkey for <paramref name="user"/>, excluding their others.</summary>
    PasskeyChallenge BeginRegistration(PasskeyRelyingParty relyingParty, UserRecord user, byte[] userHandle);

    Task<NewPasskey> CompleteRegistration(string state, string responseJson, CancellationToken cancellationToken);

    /// <summary>Starts a sign-in with any discoverable passkey of this relying party.</summary>
    PasskeyChallenge BeginAssertion(PasskeyRelyingParty relyingParty);

    /// <summary>The credential ID a sign-in answer claims, to look up its passkey; null when unreadable.</summary>
    byte[]? AssertedCredentialId(string responseJson);

    /// <summary>Verifies a sign-in answer against the stored passkey and returns its new signature counter.</summary>
    Task<uint> CompleteAssertion(string state, string responseJson, UserPasskeyRecord passkey,
        CancellationToken cancellationToken);
}

public sealed class PasskeyVerificationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
