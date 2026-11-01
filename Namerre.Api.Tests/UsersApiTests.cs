using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;
using Namerre.Authentication;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class UsersApiTests : IDisposable
{
    private const string AdminPassword = "installed-admin-password";
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"namerre-users-{Guid.NewGuid():N}");
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");

    public UsersApiTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task AdministratorsManageUsersAndUsersGetOnlyTheirRolesPermissions()
    {
        using var factory = Factory();
        using var admin = await Installed(factory);

        using var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "carol", password = "carol-first-password" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var carol = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = carol.GetProperty("id").GetInt64();
        Assert.Equal(("User", true, false), (carol.GetProperty("role").GetString(),
            carol.GetProperty("hasPassword").GetBoolean(), carol.GetProperty("disabled").GetBoolean()));
        await AssertCode(await admin.PostAsJsonAsync("/api/users",
            new { username = "CAROL", password = "another-long-password" }), HttpStatusCode.Conflict, "UsernameTaken");
        await AssertCode(await admin.PostAsJsonAsync("/api/users",
            new { username = "dave", password = "short" }), HttpStatusCode.BadRequest, "WeakPassword");

        using var user = await SignIn(factory, "carol", "carol-first-password");
        var session = await user.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("User", session.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/rename-plugins")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/rename-runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/watch-folders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/rename-runs/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/rename-run-deletions",
            new { runIds = new[] { "missing" } })).StatusCode);

        // Resetting the password ends the user's sessions.
        using var reset = await admin.PutAsJsonAsync($"/api/users/{id}/password",
            new { password = "carol-second-password" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/rename-plugins")).StatusCode);

        // Promoting ends them too; the new session carries the new role.
        using var promote = await admin.PatchAsJsonAsync($"/api/users/{id}", new { role = "Admin" });
        Assert.Equal("Admin", (await promote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());
        using var promoted = await SignIn(factory, "carol", "carol-second-password");
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/users")).StatusCode);

        using var disable = await admin.PatchAsJsonAsync($"/api/users/{id}", new { disabled = true });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await promoted.GetAsync("/api/rename-plugins")).StatusCode);
        var anonymous = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new { username = "carol", password = "carol-second-password" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/users/{id}")).StatusCode);
    }

    [Fact]
    public async Task TheLastAdministratorAndOwnRoleAreProtected()
    {
        using var factory = Factory();
        using var admin = await Installed(factory);
        var self = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("userId").GetInt64();

        await AssertCode(await admin.PatchAsJsonAsync($"/api/users/{self}", new { role = "User" }),
            HttpStatusCode.Conflict, "OwnAccount");
        await AssertCode(await admin.DeleteAsync($"/api/users/{self}"), HttpStatusCode.Conflict, "OwnAccount");
        await AssertCode(await admin.DeleteAsync($"/api/users/{self}/password"), HttpStatusCode.Conflict,
            "LastSignInMethod");

        using var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "erin", role = "Admin", password = "erin-first-password" });
        var erinId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        using var erin = await SignIn(factory, "erin", "erin-first-password");

        // With a second administrator, the first can be demoted by someone else, but then erin is the last one.
        using var demote = await erin.PatchAsJsonAsync($"/api/users/{self}", new { role = "User" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        using var again = await SignIn(factory, "installed-admin", AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await again.GetAsync("/api/users")).StatusCode);
        await AssertCode(await erin.PatchAsJsonAsync($"/api/users/{erinId}", new { disabled = true }),
            HttpStatusCode.Conflict, "OwnAccount");
    }

    [Fact]
    public async Task PlansAndRunsAreAttributedToTheUserWhoStartedThem()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = Factory();
        using var admin = await Installed(factory);
        using (await admin.PostAsJsonAsync("/api/users", new { username = "frank", password = "frank-long-password" })) { }
        using var frank = await SignIn(factory, "frank", "frank-long-password");

        var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await frank.PostAsJsonAsync("/api/rename-plans", request);
        var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;
        var plan = await Poll(() => frank.GetFromJsonAsync<PlanView>("/api/rename-plans/" + accepted.Id),
            p => p!.Status != "Preparing");
        using var execute = await frank.PostAsJsonAsync($"/api/rename-plans/{plan!.Id}/execute",
            new ExecuteRequest(plan.Version, Guid.NewGuid().ToString("N")));
        var runId = (await execute.Content.ReadFromJsonAsync<JobAccepted>())!.Id;
        var run = await Poll(() => admin.GetFromJsonAsync<RunView>("/api/rename-runs/" + runId),
            r => r!.Status is not ("Queued" or "Running"));

        Assert.Equal("frank", plan.CreatedBy!.Username);
        Assert.Equal("Completed", run!.Status);
        Assert.Equal("frank", run.CreatedBy!.Username);
        // Administrators see everyone's history, attributed to its author.
        var page = (await admin.GetFromJsonAsync<RunPage>("/api/rename-runs"))!;
        Assert.Equal("frank", Assert.Single(page.Items).CreatedBy!.Username);
        var csv = await admin.GetStringAsync($"/api/rename-runs/{runId}/export?format=csv");
        Assert.Contains(",frank,", csv);
    }

    [Fact]
    public async Task UsersOnlySeeAndManageTheirOwnRenamesWhileAdministratorsSeeEveryone()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        File.WriteAllText(Path.Combine(Root, "beta.txt"), "B");
        using var factory = Factory();
        using var admin = await Installed(factory);
        foreach (var username in new[] { "frank", "grace" })
            using (await admin.PostAsJsonAsync("/api/users", new { username, password = username + "-long-password" })) { }
        using var frank = await SignIn(factory, "frank", "frank-long-password");
        using var grace = await SignIn(factory, "grace", "grace-long-password");

        var (frankPlan, frankRun) = await Rename(frank, "alpha.txt");
        var (_, adminRun) = await Rename(admin, "beta.txt");

        // Each user lists only their own runs; administrators list both, with their authors.
        Assert.Equal([frankRun], (await frank.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items.Select(r => r.Id));
        Assert.Empty((await grace.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items);
        var all = (await admin.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items;
        Assert.Equal(["installed-admin", "frank"], all.Select(r => r.CreatedBy!.Username));

        // Someone else's plan or run reads as missing, and cannot be exported, undone or recovered.
        var operation = Guid.NewGuid().ToString("N");
        foreach (var response in new[]
        {
            await grace.GetAsync("/api/rename-plans/" + frankPlan),
            await grace.PostAsJsonAsync($"/api/rename-plans/{frankPlan}/execute", new ExecuteRequest(1, operation)),
            await grace.GetAsync("/api/rename-runs/" + frankRun),
            await grace.GetAsync($"/api/rename-runs/{frankRun}/export?format=json"),
            await grace.PostAsJsonAsync($"/api/rename-runs/{frankRun}/rollback-preview", new SelectionRequest(["0"], operation)),
            await grace.GetAsync($"/api/rename-runs/{frankRun}/rollback-preview/{operation}"),
            await grace.PostAsJsonAsync($"/api/rename-runs/{frankRun}/rollback", new SelectionRequest(["0"], operation)),
            await grace.PostAsync($"/api/rename-runs/{frankRun}/recovery-preview", null),
            await frank.GetAsync("/api/rename-runs/" + adminRun)
        })
            await AssertCode(response, HttpStatusCode.NotFound, "NotFound");

        // An administrator can undo a user's rename; the rollback stays in that user's history.
        using var rollback = await admin.PostAsJsonAsync($"/api/rename-runs/{frankRun}/rollback",
            new SelectionRequest(["0"], operation));
        Assert.Equal(HttpStatusCode.Accepted, rollback.StatusCode);
        var rollbackId = (await rollback.Content.ReadFromJsonAsync<JobAccepted>())!.Id;
        var undone = await Poll(() => frank.GetFromJsonAsync<RunView>("/api/rename-runs/" + rollbackId),
            r => r!.Status is not ("Queued" or "Running"));
        Assert.Equal(("Completed", "installed-admin"), (undone!.Status, undone.CreatedBy!.Username));
        Assert.Equal(2, (await frank.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items.Count);
    }

    /// <summary>Prepares and executes an uppercase rename of one file, returning the plan and completed run IDs.</summary>
    private static async Task<(string Plan, string Run)> Rename(HttpClient client, string path)
    {
        var request = new PipelineRequest([new FileInput("0", path)],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await client.PostAsJsonAsync("/api/rename-plans", request);
        var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;
        var plan = await Poll(() => client.GetFromJsonAsync<PlanView>("/api/rename-plans/" + accepted.Id),
            p => p!.Status != "Preparing");
        using var execute = await client.PostAsJsonAsync($"/api/rename-plans/{plan!.Id}/execute",
            new ExecuteRequest(plan.Version, Guid.NewGuid().ToString("N")));
        var runId = (await execute.Content.ReadFromJsonAsync<JobAccepted>())!.Id;
        await Poll(() => client.GetFromJsonAsync<RunView>("/api/rename-runs/" + runId),
            r => r!.Status is not ("Queued" or "Running"));
        return (plan.Id, runId);
    }

    [Fact]
    public async Task MergingAUserMovesItsIdentitiesAndRunHistory()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = Factory(new()
        {
            ["Namerre:Authentication:Oidc:Authority"] = "https://keycloak.test/realms/media",
            ["Namerre:Authentication:Oidc:ClientId"] = "namerre",
            ["Namerre:Authentication:Oidc:RequiredRole"] = "namerre-user"
        });
        using var admin = await Installed(factory);
        using var created = await admin.PostAsJsonAsync("/api/users",
            new { username = "frank-2", password = "frank-long-password", subject = "sub-frank" }); // betterleaks:allow
        var source = await created.Content.ReadFromJsonAsync<JsonElement>();
        using var target = await admin.PostAsJsonAsync("/api/users",
            new { username = "frank", password = "frank-real-password" }); // betterleaks:allow
        var targetId = (await target.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        using (var provisioned = await SignIn(factory, "frank-2", "frank-long-password"))
        {
            var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
                [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
            using var prepare = await provisioned.PostAsJsonAsync("/api/rename-plans", request);
            var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;
            var plan = await Poll(() => provisioned.GetFromJsonAsync<PlanView>("/api/rename-plans/" + accepted.Id),
                p => p!.Status != "Preparing");
            using var execute = await provisioned.PostAsJsonAsync($"/api/rename-plans/{plan!.Id}/execute",
                new ExecuteRequest(plan.Version, Guid.NewGuid().ToString("N")));
            var runId = (await execute.Content.ReadFromJsonAsync<JobAccepted>())!.Id;
            await Poll(() => admin.GetFromJsonAsync<RunView>("/api/rename-runs/" + runId),
                r => r!.Status is not ("Queued" or "Running"));
        }

        using var merge = await admin.PostAsJsonAsync($"/api/users/{targetId}/merge",
            new { sourceUserId = source.GetProperty("id").GetInt64() });

        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        var merged = await merge.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, merged.GetProperty("logins").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/users/{source.GetProperty("id").GetInt64()}")).StatusCode);
        var run = Assert.Single((await admin.GetFromJsonAsync<RunPage>("/api/rename-runs"))!.Items);
        Assert.Equal((targetId, "frank"), (run.CreatedBy!.Id, run.CreatedBy.Username));
        var plan2 = (await admin.GetFromJsonAsync<PlanView>("/api/rename-plans/" + run.PlanId))!;
        Assert.Equal("frank", plan2.CreatedBy!.Username);
    }

    private static async Task<T> Poll<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        for (var i = 0; i < 100; i++)
        {
            var value = await read();
            if (done(value)) return value;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }

    private static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    private static async Task<HttpClient> Installed(WebApplicationFactory<Program> factory)
    {
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var token = factory.Services.GetRequiredService<UserAccounts>().SetupToken;
        using var setup = await client.PostAsJsonAsync("/api/auth/setup",
            new { token, username = "installed-admin", password = AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        return await SignIn(factory, "installed-admin", AdminPassword);
    }

    private static async Task<HttpClient> SignIn(WebApplicationFactory<Program> factory, string username,
        string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        return client;
    }

    private WebApplicationFactory<Program> Factory(Dictionary<string, string?>? settings = null)
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:Authentication:Username"] = "",
                ["Namerre:Authentication:PasswordHash"] = "",
                ["Namerre:RootPath"] = Root,
                ["Namerre:DataPath"] = Data
            }).AddInMemoryCollection(settings ?? [])));

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }
}
