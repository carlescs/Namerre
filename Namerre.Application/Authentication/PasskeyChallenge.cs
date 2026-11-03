namespace Namerre.Application.Authentication;

/// <summary>
/// One WebAuthn ceremony: <see cref="OptionsJson"/> goes to the browser, and <see cref="State"/> must come back,
/// unchanged and protected from tampering, to verify the browser's answer.
/// </summary>
public sealed record PasskeyChallenge(string OptionsJson, string State);
