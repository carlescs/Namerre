using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class DataProtectionEncryptionTests : IDisposable
{
    private const string PfxPassword = "dp-pfx-test-password"; // betterleaks:allow
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"namerre-dp-{Guid.NewGuid():N}");
    private string Data => Path.Combine(workspace, "data");
    private string Keys => Path.Combine(Data, "keys");

    public DataProtectionEncryptionTests() => Directory.CreateDirectory(Path.Combine(workspace, "media"));

    public void Dispose() => Directory.Delete(workspace, recursive: true);

    private string CreatePfx(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=namerre-keys", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(workspace, name);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, PfxPassword));
        return path;
    }

    private WebApplicationFactory<Program> Factory(Dictionary<string, string?> settings)
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Namerre:RootPath"] = Path.Combine(workspace, "media"),
                    ["Namerre:DataPath"] = Data
                };
                foreach (var pair in settings) values[pair.Key] = pair.Value;
                config.AddInMemoryCollection(values);
            }));

    private static async Task<string> SignIn(WebApplicationFactory<Program> factory)
    {
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<HttpStatusCode> Folders(WebApplicationFactory<Program> factory, string cookie)
    {
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var response = await client.GetAsync("/api/folders");
        return response.StatusCode;
    }

    private string[] KeyFiles() => Directory.GetFiles(Keys, "key-*.xml").Select(File.ReadAllText).ToArray();

    [Fact]
    public async Task KeysAreEncryptedWithTheCertificateAndSessionsSurviveRestart()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Namerre:DataProtection:Certificate"] = CreatePfx("keys.pfx"),
            ["Namerre:DataProtection:CertificatePassword"] = PfxPassword
        };
        string cookie;
        using (var factory = Factory(settings))
            cookie = await SignIn(factory);

        var keys = KeyFiles();
        Assert.NotEmpty(keys);
        Assert.All(keys, xml =>
        {
            Assert.Contains("encryptedSecret", xml);
            Assert.DoesNotContain("<masterKey", xml);
        });

        using var restarted = Factory(settings);
        Assert.Equal(HttpStatusCode.OK, await Folders(restarted, cookie));
    }

    [Fact]
    public async Task EnablingEncryptionKeepsExistingKeysReadableAndEncryptsNewOnes()
    {
        string cookie;
        using (var plain = Factory([]))
            cookie = await SignIn(plain);
        Assert.All(KeyFiles(), xml => Assert.Contains("<masterKey", xml));

        var settings = new Dictionary<string, string?>
        {
            ["Namerre:DataProtection:Certificate"] = CreatePfx("keys.pfx"),
            ["Namerre:DataProtection:CertificatePassword"] = PfxPassword
        };
        using var encrypted = Factory(settings);
        Assert.Equal(HttpStatusCode.OK, await Folders(encrypted, cookie));
    }

    [Fact]
    public async Task RetiredCertificatesStillDecryptAfterRotation()
    {
        var first = CreatePfx("first.pfx");
        var second = CreatePfx("second.pfx");
        string cookie;
        using (var factory = Factory(new()
        {
            ["Namerre:DataProtection:Certificate"] = first,
            ["Namerre:DataProtection:CertificatePassword"] = PfxPassword
        }))
            cookie = await SignIn(factory);

        using var rotated = Factory(new()
        {
            ["Namerre:DataProtection:Certificate"] = second,
            ["Namerre:DataProtection:CertificatePassword"] = PfxPassword,
            ["Namerre:DataProtection:PreviousCertificates:0:Path"] = first,
            ["Namerre:DataProtection:PreviousCertificates:0:Password"] = PfxPassword
        });
        Assert.Equal(HttpStatusCode.OK, await Folders(rotated, cookie));
    }

    [Fact]
    public void MissingCertificateFileFailsStartupWithAClearMessage()
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = Factory(new()
            {
                ["Namerre:DataProtection:Certificate"] = Path.Combine(workspace, "missing.pfx")
            });
            factory.CreateClient();
        });
        Assert.Contains("Namerre:DataProtection:Certificate", exception.ToString());
        Assert.Contains("does not exist", exception.ToString());
    }

    [Fact]
    public void WrongPasswordFailsStartupWithoutLeakingIt()
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = Factory(new()
            {
                ["Namerre:DataProtection:Certificate"] = CreatePfx("keys.pfx"),
                ["Namerre:DataProtection:CertificatePassword"] = "not-the-password" // betterleaks:allow
            });
            factory.CreateClient();
        });
        Assert.Contains("password is wrong", exception.ToString());
        Assert.DoesNotContain("not-the-password", exception.ToString()); // betterleaks:allow
    }
}
