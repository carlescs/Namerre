using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Presets;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class RenamePresetsApiTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".preset-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");

    public RenamePresetsApiTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task PresetsCanBeCreatedListedLoadedUpdatedRenamedAndDeleted()
    {
        using var factory = Factory();
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);

        using var created = await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("TV shows",
            [Step("upper", new StepCondition(["mkv"]))], VerifyContent: true, OnCollision: "AddNumber"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var preset = (await created.Content.ReadFromJsonAsync<PresetView>())!;
        Assert.Equal($"/api/rename-presets/{preset.Id}", created.Headers.Location!.OriginalString);
        Assert.Equal(("TV shows", true, true, "AddNumber"),
            (preset.Name, preset.VerifyContent, preset.DeleteEmptyFolders, preset.OnCollision));

        var list = (await client.GetFromJsonAsync<PresetSummary[]>("/api/rename-presets"))!;
        Assert.Equal((preset.Id, "TV shows", 1), (Assert.Single(list).Id, list[0].Name, list[0].Steps));

        var loaded = (await client.GetFromJsonAsync<PresetView>($"/api/rename-presets/{preset.Id}"))!;
        Assert.Equal(["mkv"], loaded.Plugins.Single().Condition!.Extensions!);
        Assert.Equal("upper", loaded.Plugins.Single().Options.GetProperty("mode").GetString());
        Assert.Empty(loaded.UnknownSteps);

        using var updated = await client.PutAsJsonAsync($"/api/rename-presets/{preset.Id}",
            new SavePresetRequest("TV", [Step("lower"), Step("upper") with { InstanceId = "second" }]));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<PresetView>())!.Plugins.Count);

        using var renamed = await client.PatchAsJsonAsync($"/api/rename-presets/{preset.Id}", new RenamePresetRequest(" Series "));
        var renamedView = (await renamed.Content.ReadFromJsonAsync<PresetView>())!;
        Assert.Equal(("Series", 2), (renamedView.Name, renamedView.Plugins.Count));
        Assert.Equal(preset.CreatedAt, renamedView.CreatedAt);

        using var deleted = await client.DeleteAsync($"/api/rename-presets/{preset.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync($"/api/rename-presets/{preset.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task NamesAreUniqueIgnoringCaseAndInvalidPresetsAreRejected()
    {
        using var factory = Factory();
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        (await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("Movies", []))).EnsureSuccessStatusCode();
        var other = (await (await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("Other", [])))
            .Content.ReadFromJsonAsync<PresetView>())!;

        await AssertProblem(await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("MOVIES", [])),
            HttpStatusCode.Conflict, "PresetNameTaken");
        await AssertProblem(await client.PatchAsJsonAsync($"/api/rename-presets/{other.Id}", new RenamePresetRequest("movies")),
            HttpStatusCode.Conflict, "PresetNameTaken");
        await AssertProblem(await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest(" ", [])),
            HttpStatusCode.BadRequest, "InvalidPresetName");
        await AssertProblem(await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("Bad",
            [Step("upper") with { TypeId = "unknown" }])), HttpStatusCode.BadRequest, "InvalidPlugin");
        await AssertProblem(await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("Bad",
            [Step("upper", new StepCondition(Pattern: "["))])), HttpStatusCode.BadRequest, "InvalidCondition");
        await AssertProblem(await client.PostAsJsonAsync("/api/rename-presets", new SavePresetRequest("Bad",
            Enumerable.Range(0, RenameLimits.DefaultSteps + 1).Select(i => Step("upper") with { InstanceId = $"s{i}" }).ToArray())),
            HttpStatusCode.BadRequest, "BatchLimit");
    }

    [Fact]
    public async Task StepsTheServerNoLongerKnowsAreFlaggedInsteadOfFailing()
    {
        using var factory = Factory();
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);
        var preset = (await (await client.PostAsJsonAsync("/api/rename-presets",
            new SavePresetRequest("Old", [Step("upper"), Step("lower") with { InstanceId = "retired" }])))
            .Content.ReadFromJsonAsync<PresetView>())!;
        // Simulate a plugin version that a later release removed.
        using (var db = new RenameDbContext(new DbContextOptionsBuilder<RenameDbContext>()
                   .UseSqlite($"Data Source={Path.Combine(Data, "renaming.sqlite3")};Pooling=False").Options))
        {
            var row = db.Presets.Single();
            row.Json = row.Json.Replace("\"instanceId\":\"retired\",\"typeId\":\"case\",\"version\":1",
                "\"instanceId\":\"retired\",\"typeId\":\"case\",\"version\":9");
            db.SaveChanges();
        }

        var loaded = (await client.GetFromJsonAsync<PresetView>($"/api/rename-presets/{preset.Id}"))!;

        Assert.Equal(["retired"], loaded.UnknownSteps);
        Assert.Equal(2, loaded.Plugins.Count);
    }

    [Fact]
    public async Task PresetsRequireSignIn()
    {
        using var factory = Factory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        using var response = await client.GetAsync("/api/rename-presets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static PluginInstance Step(string mode, StepCondition? condition = null)
        => new("step", "case", 1, true, JsonSerializer.SerializeToElement(new { mode }), condition);

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, problem.GetProperty("code").GetString());
        }
    }

    private WebApplicationFactory<Program> Factory()
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:RootPath"] = Root, ["Namerre:DataPath"] = Data
            })));

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }
}
