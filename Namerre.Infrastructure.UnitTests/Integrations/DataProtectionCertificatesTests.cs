using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Namerre.Infrastructure.Configuration;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Integrations;

public sealed class DataProtectionCertificatesTests : IDisposable
{
    private const string PfxPassword = "pfx-test-password"; // betterleaks:allow
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"namerre-dp-{Guid.NewGuid():N}");

    public DataProtectionCertificatesTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string Pfx(string name, string? password, bool withPrivateKey = true, bool ecdsa = false)
    {
        var request = ecdsa
            ? new CertificateRequest("CN=namerre-test", ECDsa.Create(ECCurve.NamedCurves.nistP256), HashAlgorithmName.SHA256)
            : new CertificateRequest("CN=namerre-test", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(directory, name);
        if (withPrivateKey)
        {
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        }
        else
        {
            // A PKCS#12 bundle that carries only the public certificate.
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx));
        }
        return path;
    }

    private static string? Problem(DataProtectionOptions options) => DataProtectionCertificates.Problem(options);

    [Fact]
    public void DisabledByDefault()
    {
        Assert.Null(Problem(new()));
        var (current, previous) = DataProtectionCertificates.Load(new());
        Assert.Null(current);
        Assert.Empty(previous);
    }

    [Fact]
    public void AValidPfxLoadsWithItsPrivateKey()
    {
        var (current, previous) = DataProtectionCertificates.Load(
            new() { Certificate = Pfx("a.pfx", PfxPassword), CertificatePassword = PfxPassword });
        Assert.True(current!.HasPrivateKey);
        Assert.Empty(previous);
        current.Dispose();
    }

    [Fact]
    public void APfxWithoutPasswordLoads()
        => Assert.Null(Problem(new() { Certificate = Pfx("none.pfx", null) }));

    [Fact]
    public void MissingFileIsReported()
        => Assert.Contains("does not exist", Problem(new() { Certificate = Path.Combine(directory, "nope.pfx") }));

    [Fact]
    public void WrongPasswordIsReportedWithoutEchoingIt()
    {
        var problem = Problem(new() { Certificate = Pfx("b.pfx", PfxPassword), CertificatePassword = "wrong-guess" }); // betterleaks:allow
        Assert.Contains("password is wrong", problem);
        Assert.DoesNotContain("wrong-guess", problem); // betterleaks:allow
        Assert.DoesNotContain(PfxPassword, problem);
    }

    [Fact]
    public void NotAPfxIsReported()
    {
        var path = Path.Combine(directory, "garbage.pfx");
        File.WriteAllText(path, "not a certificate");
        Assert.Contains("not a valid PFX", Problem(new() { Certificate = path }));
    }

    [Fact]
    public void CertificateWithoutPrivateKeyIsReported()
        => Assert.Contains("no private key", Problem(new() { Certificate = Pfx("pub.cer", null, withPrivateKey: false) }));

    [Fact]
    public void NonRsaKeysAreReported()
        => Assert.Contains("RSA", Problem(new() { Certificate = Pfx("ec.pfx", PfxPassword, ecdsa: true), CertificatePassword = PfxPassword }));

    [Fact]
    public void PasswordOrPreviousCertificatesWithoutACertificateAreReported()
    {
        Assert.Contains("without", Problem(new() { CertificatePassword = PfxPassword }));
        Assert.Contains("requires", Problem(new()
        {
            PreviousCertificates = [new() { Path = Pfx("old.pfx", PfxPassword), Password = PfxPassword }]
        }));
    }

    [Fact]
    public void PreviousCertificatesAreValidatedByIndex()
    {
        var options = new DataProtectionOptions
        {
            Certificate = Pfx("cur.pfx", PfxPassword),
            CertificatePassword = PfxPassword,
            PreviousCertificates = [new() { Path = Pfx("old.pfx", PfxPassword), Password = "bad" }] // betterleaks:allow
        };
        Assert.Contains("PreviousCertificates:0:Path", Problem(options));
        options.PreviousCertificates[0].Password = PfxPassword;
        Assert.Null(Problem(options));
        var (current, previous) = DataProtectionCertificates.Load(options);
        Assert.Single(previous);
        current!.Dispose();
        previous[0].Dispose();
    }

    [Fact]
    public void LoadThrowsTheProblem()
        => Assert.Contains("does not exist", Assert.Throws<InvalidOperationException>(
            () => DataProtectionCertificates.Load(new() { Certificate = Path.Combine(directory, "x.pfx") })).Message);

    [Fact]
    public void ValidatorFailsStartupOnAProblem()
    {
        var validator = new DataProtectionOptionsValidator();
        Assert.True(validator.Validate(null, new()).Succeeded);
        var result = validator.Validate(null,
            new() { DataProtection = new() { Certificate = Path.Combine(directory, "x.pfx") } });
        Assert.True(result.Failed);
    }
}
