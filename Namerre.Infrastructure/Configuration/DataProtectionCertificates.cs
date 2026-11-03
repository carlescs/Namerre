using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Namerre.Infrastructure.Configuration;

/// <summary>
/// Loads the PFX files that protect the Data Protection key ring. Failures say which setting is wrong and never
/// include a password.
/// </summary>
public static class DataProtectionCertificates
{
    private const string Section = "Namerre:DataProtection";

    /// <summary>The current certificate (null when encryption is off) and the retired ones; throws with a clear message.</summary>
    public static (X509Certificate2? Current, IReadOnlyList<X509Certificate2> Previous) Load(DataProtectionOptions options)
    {
        var problem = Inspect(options, out var current, out var previous);
        if (problem is not null)
            throw new InvalidOperationException(problem);
        return (current, previous);
    }

    /// <summary>The first configuration problem, or <see langword="null"/> when the settings are usable.</summary>
    public static string? Problem(DataProtectionOptions options)
    {
        var problem = Inspect(options, out var current, out var previous);
        current?.Dispose();
        foreach (var certificate in previous) certificate.Dispose();
        return problem;
    }

    private static string? Inspect(DataProtectionOptions options, out X509Certificate2? current,
        out List<X509Certificate2> previous)
    {
        current = null;
        previous = [];
        if (string.IsNullOrWhiteSpace(options.Certificate))
        {
            if (!string.IsNullOrWhiteSpace(options.CertificatePassword))
                return $"{Section}:CertificatePassword is set without {Section}:Certificate.";
            if (options.PreviousCertificates.Count > 0)
                return $"{Section}:PreviousCertificates requires {Section}:Certificate.";
            return null;
        }

        var failure = Open($"{Section}:Certificate", options.Certificate, options.CertificatePassword, out current);
        if (failure is not null) return failure;
        for (var index = 0; index < options.PreviousCertificates.Count; index++)
        {
            var entry = options.PreviousCertificates[index];
            failure = Open($"{Section}:PreviousCertificates:{index}:Path", entry.Path, entry.Password, out var old);
            if (failure is not null) return failure;
            previous.Add(old!);
        }
        return null;
    }

    private static string? Open(string setting, string? path, string? password, out X509Certificate2? certificate)
    {
        certificate = null;
        if (string.IsNullOrWhiteSpace(path)) return $"{setting} must not be empty.";
        if (!File.Exists(path)) return $"{setting} '{path}' does not exist or is not a file.";
        try
        {
            // Default key storage works on both platforms: Windows needs a persistable key for the XML decryptor
            // (ephemeral keys are not reliable there), and Linux keeps the key in memory only.
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path,
                string.IsNullOrEmpty(password) ? null : password, X509KeyStorageFlags.DefaultKeySet);
        }
        catch (CryptographicException)
        {
            return $"{setting} '{path}' is not a valid PFX file, or its password is wrong "
                + "(check the matching password setting).";
        }
        catch (IOException exception)
        {
            return $"{setting} '{path}' cannot be read: {exception.Message}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{setting} '{path}' cannot be read: access denied.";
        }
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            certificate = null;
            return $"{setting} '{path}' has no private key; export the certificate with its private key.";
        }
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificate.Dispose();
            certificate = null;
            return $"{setting} '{path}' must hold an RSA key; other key types cannot encrypt the key ring.";
        }
        return null;
    }
}
