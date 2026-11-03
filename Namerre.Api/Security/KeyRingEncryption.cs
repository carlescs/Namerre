using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Security;

/// <summary>
/// The certificates that protect the Data Protection key ring, loaded from <c>Namerre:DataProtection</c> on first use.
/// ASP.NET Core's own <c>ProtectKeysWithCertificate</c> needs the certificate while services are being registered,
/// before the final configuration (user secrets, environment, test overrides) is known, so the same
/// <see cref="EncryptedXml"/> scheme is applied here from <see cref="IOptions{TOptions}"/> instead.
/// </summary>
internal sealed class KeyRingCertificates(IOptions<NamerreOptions> options)
{
    private readonly Lazy<(X509Certificate2? Current, IReadOnlyList<X509Certificate2> Previous)> loaded
        = new(() => DataProtectionCertificates.Load(options.Value.DataProtection));

    public X509Certificate2? Current => loaded.Value.Current;

    /// <summary>The current certificate followed by the retired ones; any of them can decrypt.</summary>
    public IReadOnlyList<X509Certificate2> Decrypting
        => Current is null ? [] : [Current, .. loaded.Value.Previous];
}

/// <summary>
/// Encrypts each key's XML with the current certificate: XML Encryption with AES-256-CBC and an RSA (PKCS#1 v1.5)
/// wrapped key, the same format <c>ProtectKeysWithCertificate</c> writes.
/// </summary>
internal sealed class CertificateKeyEncryptor(KeyRingCertificates certificates) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);
        var certificate = certificates.Current
            ?? throw new InvalidOperationException("Namerre:DataProtection:Certificate is not configured.");
        var document = new XmlDocument();
        document.Load(plaintextElement.CreateReader());
        var encrypted = new EncryptedXml(document).Encrypt(document.DocumentElement!, certificate);
        return new EncryptedXmlInfo(XElement.Parse(encrypted.GetXml().OuterXml), typeof(CertificateKeyDecryptor));
    }
}

/// <summary>Decrypts keys with the current or any retired certificate; the key ring instantiates it by type name.</summary>
internal sealed class CertificateKeyDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);
        var candidates = services.GetRequiredService<KeyRingCertificates>().Decrypting;
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "Data Protection keys are encrypted, but Namerre:DataProtection:Certificate is not configured.");
        var document = new XmlDocument();
        document.Load(encryptedElement.CreateReader());
        new CertificateEncryptedXml(candidates, document).DecryptDocument();
        return XElement.Parse(document.DocumentElement!.OuterXml);
    }

    private sealed class CertificateEncryptedXml(IReadOnlyList<X509Certificate2> certificates, XmlDocument document)
        : EncryptedXml(document)
    {
        public override SymmetricAlgorithm? GetDecryptionKey(EncryptedData encryptedData, string? symmetricAlgorithmUri)
        {
            var wrapped = encryptedData.KeyInfo?.OfType<KeyInfoEncryptedKey>().Select(key => key.EncryptedKey).FirstOrDefault();
            if (wrapped?.CipherData?.CipherValue is not { } cipher)
                return base.GetDecryptionKey(encryptedData, symmetricAlgorithmUri);
            var useOaep = wrapped.EncryptionMethod?.KeyAlgorithm == XmlEncRSAOAEPUrl;
            var named = wrapped.KeyInfo?.OfType<KeyInfoX509Data>()
                .SelectMany(data => data.Certificates?.Cast<X509Certificate2>() ?? [])
                .Select(certificate => certificate.Thumbprint).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? [];
            foreach (var certificate in certificates)
            {
                if (named.Count > 0 && !named.Contains(certificate.Thumbprint)) continue;
                using var rsa = certificate.GetRSAPrivateKey();
                if (rsa is null) continue;
                var key = Aes.Create();
                key.Key = DecryptKey(cipher, rsa, useOaep);
                return key;
            }
            throw new CryptographicException("None of the configured Data Protection certificates can decrypt this key.");
        }
    }
}
