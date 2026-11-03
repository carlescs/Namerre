using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Namerre.Testing;

/// <summary>
/// A software WebAuthn authenticator holding one ES256 passkey, answering registration and sign-in options the way a
/// browser does (WebAuthn JSON). Also linked into Namerre.Api.Tests.
/// </summary>
internal sealed class SoftwareAuthenticator(string origin) : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    public byte[]? UserHandle { get; private set; }
    public uint Counter { get; set; }
    /// <summary>Whether the authenticator reports user verification (a PIN or biometric).</summary>
    public bool Verifies { get; set; } = true;
    /// <summary>The origin the browser reports; a phishing site would report its own.</summary>
    public string Origin { get; set; } = origin;

    /// <summary>Answers <c>navigator.credentials.create</c> options.</summary>
    public string Create(JsonElement options)
    {
        UserHandle = Base64Url(options.GetProperty("user").GetProperty("id").GetString()!);
        var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
        var clientData = ClientData("webauthn.create", options.GetProperty("challenge").GetString()!);
        var parameters = key.ExportParameters(false);
        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1);
        cose.WriteInt32(2); // EC2
        cose.WriteInt32(3);
        cose.WriteInt32(-7); // ES256
        cose.WriteInt32(-1);
        cose.WriteInt32(1); // P-256
        cose.WriteInt32(-2);
        cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3);
        cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();
        var credentialData = new byte[16 + 2 + CredentialId.Length];
        BinaryPrimitives.WriteUInt16BigEndian(credentialData.AsSpan(16), (ushort)CredentialId.Length);
        CredentialId.CopyTo(credentialData, 18);
        var authenticatorData = AuthenticatorData(rpId, 0x40, [.. credentialData, .. cose.Encode()]);

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();
        return Credential(new JsonObject
        {
            ["attestationObject"] = Base64Url(attestation.Encode()),
            ["clientDataJSON"] = Base64Url(clientData),
            ["transports"] = new JsonArray("internal")
        });
    }

    /// <summary>Answers <c>navigator.credentials.get</c> options.</summary>
    public string Get(JsonElement options)
    {
        var rpId = options.GetProperty("rpId").GetString()!;
        var clientData = ClientData("webauthn.get", options.GetProperty("challenge").GetString()!);
        Counter++;
        var authenticatorData = AuthenticatorData(rpId, 0, []);
        var signature = key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return Credential(new JsonObject
        {
            ["authenticatorData"] = Base64Url(authenticatorData),
            ["clientDataJSON"] = Base64Url(clientData),
            ["signature"] = Base64Url(signature),
            ["userHandle"] = Base64Url(UserHandle ?? throw new InvalidOperationException("Register first."))
        });
    }

    public void Dispose() => key.Dispose();

    private byte[] AuthenticatorData(string rpId, byte extraFlags, byte[] tail)
    {
        // User present, optionally user verified, plus attested credential data on registration.
        var flags = (byte)(0x01 | (Verifies ? 0x04 : 0) | extraFlags);
        var data = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(data, 0);
        data[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33), Counter);
        return [.. data, .. tail];
    }

    private byte[] ClientData(string type, string challenge) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        type,
        challenge,
        origin = Origin,
        crossOrigin = false
    });

    private string Credential(JsonObject response) => new JsonObject
    {
        ["id"] = Base64Url(CredentialId),
        ["rawId"] = Base64Url(CredentialId),
        ["type"] = "public-key",
        ["response"] = response,
        ["clientExtensionResults"] = new JsonObject()
    }.ToJsonString();

    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
