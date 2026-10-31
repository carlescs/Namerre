using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Presets;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class PresetServiceTests
{
    private readonly IPresetStore store = Substitute.For<IPresetStore>();
    private readonly PipelineEngine engine = TestPipeline.Create(Substitute.For<ITmdbMetadata>(), Substitute.For<IRenameFileSystem>());

    [Fact]
    public void CreateTrimsTheNameAndStoresAnIndependentCopy()
    {
        using var document = JsonDocument.Parse("""{"mode":"upper"}""");
        var view = Service().Create(new(" Movies ", [new("a", "case", 1, true, document.RootElement)]));
        Assert.Equal("Movies", view.Name);
        store.Received().Add(Arg.Is<PresetRecord>(p => p.Name == "Movies" && p.Id == view.Id && p.Plugins.Count == 1));
        // The stored options outlive the request's JSON document.
        document.Dispose();
        Assert.Equal("upper", view.Plugins[0].Options.GetProperty("mode").GetString());
    }

    [Fact]
    public void CreateStopsAtThePresetLimit()
    {
        store.Count().Returns(PresetService.MaxPresets);
        Assert.Equal("PresetLimit", Assert.Throws<RenameException>(() => Service().Create(new("x", []))).Code);
        store.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tname")]
    public void InvalidNamesAreRejected(string name)
        => Assert.Equal("InvalidPresetName", Assert.Throws<RenameException>(() => Service().Create(new(name, []))).Code);

    [Fact]
    public void LongNamesAndBadCollisionModesAreRejected()
    {
        Assert.Throws<RenameException>(() => Service().Create(new(new string('x', PresetService.MaxNameLength + 1), [])));
        Assert.Equal("InvalidCollisionMode",
            Assert.Throws<RenameException>(() => Service().Create(new("x", [], OnCollision: "Overwrite"))).Code);
        Assert.Equal("InvalidPreset", Assert.Throws<RenameException>(() => Service().Create(new("x", null!))).Code);
    }

    [Fact]
    public void ReadsFlagUnknownStepsAndPipelinesRefuseThem()
    {
        var retired = new PluginInstance("old", "case", 7, true, JsonSerializer.SerializeToElement(new { }));
        store.Get("p").Returns(new PresetRecord("p", "Old", [retired], false, true, null, default, default));
        var view = Service().Get("p");
        Assert.Equal(["old"], view.UnknownSteps);
        Assert.Equal("PresetOutdated", Assert.Throws<RenameException>(
            () => Service().Pipeline(view, [new("f", "a.txt")], false)).Code);
    }

    [Fact]
    public void PipelinesCarryThePresetOptions()
    {
        var step = new PluginInstance("s", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }));
        store.Get("p").Returns(new PresetRecord("p", "P", [step], true, false, "AddNumber", default, default));
        var request = Service().Pipeline(Service().Get("p"), [new("f", "a.txt")], true);
        Assert.Equal((true, false, "AddNumber", true), (request.VerifyContent, request.DeleteEmptyFolders,
            request.OnCollision, request.IncludeCompanions));
        Assert.Same(step, Assert.Single(request.Plugins));
        Assert.Empty(request.Selections!);
    }

    [Fact]
    public void RenameKeepsTheContentAndUpdatesTheTimestamp()
    {
        var created = DateTimeOffset.UnixEpoch;
        store.Get("p").Returns(new PresetRecord("p", "Old", [], true, false, null, created, created));
        var view = Service().Rename("p", new("New"));
        Assert.Equal(("New", true, created), (view.Name, view.VerifyContent, view.CreatedAt));
        Assert.True(view.UpdatedAt > created);
        store.Received().Replace(Arg.Is<PresetRecord>(p => p.Name == "New" && p.VerifyContent));
    }

    [Fact]
    public void ListsByName()
    {
        store.List().Returns([
            new PresetRecord("b", "beta", [], false, true, null, default, default),
            new PresetRecord("a", "Alpha", [], false, true, null, default, default)
        ]);
        Assert.Equal(["Alpha", "beta"], Service().List().Select(p => p.Name));
    }

    private PresetService Service() => new(store, engine);
}
