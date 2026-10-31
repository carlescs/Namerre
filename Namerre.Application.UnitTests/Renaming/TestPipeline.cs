using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;

namespace Namerre.Application.UnitTests.Renaming;

internal static class TestPipeline
{
    /// <summary>The engine with the built-in plugins, in the order <c>AddApplication()</c> registers them.</summary>
    public static PipelineEngine Create(ITmdbMetadata metadata, IRenameFileSystem fileSystem)
        => new(fileSystem, [new LiteralPlugin(), new RegexPlugin(), new CasePlugin(), new AffixPlugin(),
            new PadNumbersPlugin(), new CounterPlugin(), new DatePlugin(),
            new TmdbPlugin(metadata, fileSystem)]);
}
