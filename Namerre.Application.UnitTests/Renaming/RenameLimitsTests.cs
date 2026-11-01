using System.Text.Json;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class RenameLimitsTests
{
    private readonly IRenameFileSystem fileSystem = Substitute.For<IRenameFileSystem>();

    [Theory]
    [InlineData(200, 32, 32, true)]
    [InlineData(2000, 64, 256, true)]
    [InlineData(1, 1, 1, true)]
    [InlineData(0, 32, 32, false)]
    [InlineData(2001, 32, 32, false)]
    [InlineData(200, 65, 32, false)]
    [InlineData(200, 32, 257, false)]
    [InlineData(200, -1, 32, false)]
    public void LimitsMustStayBetweenOneAndTheirCeiling(int files, int steps, int queued, bool valid)
        => Assert.Equal(valid, new RenameLimits(files, steps, queued).IsValid);

    [Theory]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(0, false)]
    [InlineData(17, false)]
    public void ConcurrentPreviewsMustStayBetweenOneAndTheirCeiling(int previews, bool valid)
        => Assert.Equal(valid, new RenameLimits(MaxConcurrentPreviews: previews).IsValid);

    [Fact]
    public void DefaultsAreTheHistoricLimits()
        => Assert.Equal(new RenameLimits(200, 32, 32, 4), RenameLimits.Default);

    [Fact]
    public void TheEngineEnforcesConfiguredFileAndStepLimits()
    {
        var engine = new PipelineEngine(fileSystem, [new CasePlugin()], new RenameLimits(2, 1, 1));
        var step = new PluginInstance("a", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }));

        engine.ValidateRequest(Request(2, step));
        var files = Assert.Throws<RenameException>(() => engine.ValidateRequest(Request(3, step)));
        var steps = Assert.Throws<RenameException>(() => engine.ValidateRequest(Request(1, step, step with { InstanceId = "b" })));

        Assert.Equal(("BatchLimit", "BatchLimit"), (files.Code, steps.Code));
        Assert.Equal("Supply 1–2 files and at most 1 plugin instances.", files.Message);
    }

    private static PipelineRequest Request(int files, params PluginInstance[] plugins)
        => new(Enumerable.Range(0, files).Select(i => new FileInput(i.ToString(), $"f{i}.txt")).ToArray(), plugins);
}
