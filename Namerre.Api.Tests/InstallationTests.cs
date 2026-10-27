using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Authentication;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class InstallationTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"namerre-install-{Guid.NewGuid():N}");
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");
    private const string Username = "installed-admin";
    private const string Password = "a-unique-installed-password";

    public InstallationTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task UnconfiguredInstallationExposesOnlySetupStatusNotTheToken()
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        using var response = await client.GetAsync("/api/auth/session");
        var session = await response.Content.ReadFromJsonAsync<Session>();
        Assert.True(session!.RequiresSetup);
        Assert.Null(session.Username);
        var token = Token(factory);
        Assert.Equal(64, token.Length);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var denied = await client.GetAsync("/api/folders");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var database = OpenDatabase();
        Assert.Empty(database.Administrators);
    }

    [Fact]
    public async Task InstallationRequiresCsrfAndTheCurrentConsoleToken()
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        var token = Token(factory);
        using var missingCsrf = await client.PostAsJsonAsync("/api/auth/setup", new { token, Username, Password });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var invalid = await client.PostAsJsonAsync("/api/auth/setup",
            new { token = new string('0', 64), Username, Password });
        Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        using var database = OpenDatabase();
        Assert.Empty(database.Administrators);
        Assert.True(factory.Services.GetRequiredService<AdminAccount>().RequiresSetup);
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData(Username, "")]
    [InlineData(Username, "   ")]
    public async Task InstallationRejectsInvalidCredentials(string username, string password)
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var response = await client.PostAsJsonAsync("/api/auth/setup",
            new { token = Token(factory), username, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var database = OpenDatabase();
        Assert.Empty(database.Administrators);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("eleven char")]
    public async Task ShortPasswordsAreRejectedAndTheTokenStaysValid(string password)
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var token = Token(factory);
        using var setup = await client.PostAsJsonAsync("/api/auth/setup", new { token, Username, password });
        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.Contains("at least 12 characters", await setup.Content.ReadAsStringAsync());
        using (var database = OpenDatabase())
            Assert.Empty(database.Administrators);

        using var retry = await client.PostAsJsonAsync("/api/auth/setup", new { token, Username, Password });
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task SessionsSurviveARestartBecauseDataProtectionKeysArePersisted()
    {
        string cookie;
        using (var factory = Factory())
        using (var client = AuthWebApplicationFactory.AnonymousClient(factory))
        {
            await AuthWebApplicationFactory.RefreshCsrf(client);
            using var setup = await client.PostAsJsonAsync("/api/auth/setup",
                new { token = Token(factory), Username, Password });
            Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { Username, Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            cookie = login.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("__Host-namerre-session=", StringComparison.Ordinal))
                .Split(';')[0];
        }
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(Data, "keys"), "key-*.xml"));

        using (var factory = Factory())
        using (var client = AuthWebApplicationFactory.AnonymousClient(factory))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using var folders = await client.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.OK, folders.StatusCode);
        }
    }

    [Fact]
    public async Task AdministratorIsHashedInTheExistingDatabaseAndSurvivesRestart()
    {
        string token;
        using (var factory = Factory())
        using (var client = AuthWebApplicationFactory.AnonymousClient(factory))
        {
            await AuthWebApplicationFactory.RefreshCsrf(client);
            token = Token(factory);
            using var setup = await client.PostAsJsonAsync("/api/auth/setup", new { token, Username, Password });
            Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
            Assert.Null(factory.Services.GetRequiredService<AdminAccount>().SetupToken);
            using var database = OpenDatabase();
            var account = Assert.Single(database.Administrators);
            Assert.Equal(1, account.Id);
            Assert.Equal(Username, account.Username);
            Assert.NotEqual(Password, account.PasswordHash);
            Assert.True(factory.Services.GetRequiredService<AdminAccount>().Verify(Username, Password));
            Assert.False(factory.Services.GetRequiredService<AdminAccount>().Verify(Username, "incorrect"));
            Assert.NotEmpty(database.Database.GetAppliedMigrations());
        }

        using (var factory = Factory())
        using (var client = AuthWebApplicationFactory.AnonymousClient(factory))
        {
            var session = await client.GetFromJsonAsync<Session>("/api/auth/session");
            Assert.False(session!.RequiresSetup);
            Assert.Null(factory.Services.GetRequiredService<AdminAccount>().SetupToken);
            await AuthWebApplicationFactory.RefreshCsrf(client);
            using var duplicate = await client.PostAsJsonAsync("/api/auth/setup",
                new { token, username = "replacement", password = Password });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { Username, Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            using var folders = await client.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.OK, folders.StatusCode);
            using var database = OpenDatabase();
            Assert.Equal(Username, Assert.Single(database.Administrators).Username);
        }
    }

    [Fact]
    public async Task UpgradePreservesExistingHistoryAndTheDatabaseEnforcesOneAdministrator()
    {
        Directory.CreateDirectory(Data);
        using (var database = OpenDatabase())
        {
            database.GetService<IMigrator>().Migrate("20261018110609_InitialRenaming");
            // Seed with SQL: the current model has columns this older schema does not.
            database.Database.OpenConnection();
            using var seed = database.Database.GetDbConnection().CreateCommand();
            seed.CommandText = """
                INSERT INTO "Documents" ("Id", "Kind", "Status", "Json", "Order", "Revision") VALUES
                ('existing-history', 'Run', 'Completed', '{"parentRunId":null}', 1, 1),
                ('existing-rollback', 'Run', 'Completed', '{"parentRunId":"existing-history"}', 2, 1);
                """;
            seed.ExecuteNonQuery();
        }

        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var setup = await client.PostAsJsonAsync("/api/auth/setup",
            new { token = Token(factory), Username, Password });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        var store = factory.Services.GetRequiredService<Namerre.Application.Authentication.IAdministratorStore>();
        Assert.False(store.TryCreate(new("replacement", AdminAccount.HashPassword(Password))));
        using var upgraded = OpenDatabase();
        var documents = upgraded.Documents.OrderBy(d => d.Order).ToArray();
        Assert.Equal(["existing-history", "existing-rollback"], documents.Select(d => d.Id));
        Assert.Null(documents[0].ParentId);
        Assert.Equal("existing-history", documents[1].ParentId);
        Assert.Equal(Username, Assert.Single(upgraded.Administrators).Username);
        Assert.Empty(upgraded.Database.GetPendingMigrations());
        upgraded.Administrators.Add(new AdministratorAccount
        {
            Id = 2, Username = "second-admin", PasswordHash = AdminAccount.HashPassword(Password)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => upgraded.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentSetupRequestsCannotReplaceTheFirstAdministrator()
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var token = Token(factory);
        var requests = new[] { "first-admin", "second-admin" }
            .Select(username => client.PostAsJsonAsync("/api/auth/setup", new { token, username, Password }));
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            using var database = OpenDatabase();
            Assert.Single(database.Administrators);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task ConfiguredCredentialsSkipInstallationAndCannotBeOverwritten()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var session = await client.GetFromJsonAsync<Session>("/api/auth/session");
        Assert.False(session!.RequiresSetup);
        Assert.Null(factory.Services.GetRequiredService<AdminAccount>().SetupToken);
        using var setup = await client.PostAsJsonAsync("/api/auth/setup",
            new { token = new string('0', 64), Username, Password });
        Assert.Equal(HttpStatusCode.Conflict, setup.StatusCode);
    }

    [Fact]
    public async Task InstalledAdministratorCanChangeThePasswordAndOtherSessionsAreSignedOut()
    {
        const string newPassword = "a-different-installed-password";
        using (var factory = Factory())
        {
            using var client = AuthWebApplicationFactory.AnonymousClient(factory);
            using var other = AuthWebApplicationFactory.AnonymousClient(factory);
            await AuthWebApplicationFactory.RefreshCsrf(client);
            using var setup = await client.PostAsJsonAsync("/api/auth/setup",
                new { token = Token(factory), Username, Password });
            Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
            foreach (var session in new[] { client, other })
            {
                await AuthWebApplicationFactory.RefreshCsrf(session);
                using var login = await session.PostAsJsonAsync("/api/auth/login", new { Username, Password });
                Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
                await AuthWebApplicationFactory.RefreshCsrf(session);
            }
            var status = await client.GetFromJsonAsync<Session>("/api/auth/session");
            Assert.True(status!.CanChangePassword);

            using var wrong = await client.PostAsJsonAsync("/api/auth/password",
                new { currentPassword = "incorrect", newPassword });
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            using var changed = await client.PostAsJsonAsync("/api/auth/password",
                new { currentPassword = Password, newPassword });
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

            using var current = await client.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            using var signedOut = await other.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        }

        using (var factory = Factory())
        {
            Assert.True(factory.Services.GetRequiredService<AdminAccount>().Verify(Username, newPassword));
            Assert.False(factory.Services.GetRequiredService<AdminAccount>().Verify(Username, Password));
        }
    }

    [Fact]
    public async Task ConfiguredCredentialsCannotBeChangedThroughTheApi()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        var session = await client.GetFromJsonAsync<Session>("/api/auth/session");
        Assert.False(session!.CanChangePassword);
        using var change = await client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = AuthWebApplicationFactory.Password,
            newPassword = "another-password"
        });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
    }

    [Fact]
    public async Task PasswordChangeRequiresAuthenticationAndCsrf()
    {
        using var factory = new AuthWebApplicationFactory();
        using var anonymous = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(anonymous);
        using var unauthorized = await anonymous.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = AuthWebApplicationFactory.Password, newPassword = "another-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using var missingCsrf = await client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = AuthWebApplicationFactory.Password, newPassword = "another-password" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
    }

    [Fact]
    public async Task FailedDatabaseWriteDoesNotCompleteInstallationOrConsumeTheToken()
    {
        using var factory = Factory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<Namerre.Application.Authentication.IAdministratorStore>(
                new FailingStore())));
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var token = Token(factory);
        using var setup = await client.PostAsJsonAsync("/api/auth/setup", new { token, Username, Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, setup.StatusCode);
        var account = factory.Services.GetRequiredService<AdminAccount>();
        Assert.True(account.RequiresSetup);
        Assert.Equal(token, account.SetupToken);
    }

    private WebApplicationFactory<Program> Factory()
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Namerre:Authentication:Username"] = "",
                    ["Namerre:Authentication:PasswordHash"] = "",
                    ["Namerre:RootPath"] = Root,
                    ["Namerre:DataPath"] = Data
                })));

    private RenameDbContext OpenDatabase() => new(new DbContextOptionsBuilder<RenameDbContext>()
        .UseSqlite($"Data Source={Path.Combine(Data, "renaming.sqlite3")};Pooling=False").Options);

    private static string Token(WebApplicationFactory<Program> factory)
        => factory.Services.GetRequiredService<AdminAccount>().SetupToken
            ?? throw new InvalidOperationException("Expected an unconfigured administrator.");

    public void Dispose() => Directory.Delete(workspace, recursive: true);

    private sealed record Session(string? Username, bool RequiresSetup, bool CanChangePassword);

    private sealed class FailingStore : Namerre.Application.Authentication.IAdministratorStore
    {
        public Namerre.Application.Authentication.AdminCredentials? Load() => null;
        public bool TryCreate(Namerre.Application.Authentication.AdminCredentials credentials)
            => throw new Namerre.Application.Authentication.AdministratorStorageException(
                "Database is read-only.", new IOException("read-only"));
        public bool TryUpdate(Namerre.Application.Authentication.AdminCredentials current, string passwordHash)
            => throw new Namerre.Application.Authentication.AdministratorStorageException(
                "Database is read-only.", new IOException("read-only"));
    }
}
