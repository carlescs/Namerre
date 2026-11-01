using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;

namespace Namerre.Testing;

/// <summary>
/// Shared by Namerre.Application.UnitTests and (as a linked file) Namerre.Api.Tests.
/// <c>ApplicationRegistrationTests.TestPipelineMatchesTheRegisteredCatalog</c> fails when this list drifts.
/// </summary>
internal static class TestPipeline
{
    /// <summary>The engine with the built-in plugins, in the order <c>AddApplication()</c> registers them.</summary>
    public static PipelineEngine Create(ITmdbMetadata metadata, IRenameFileSystem fileSystem)
        => new(fileSystem, [new LiteralPlugin(), new ReplacementTablePlugin(), new RegexPlugin(), new CasePlugin(),
            new ExtensionCasePlugin(), new AffixPlugin(), new SeparatorsPlugin(), new ReleaseTagsPlugin(), new CharactersPlugin(),
            new InsertPlugin(), new RemovePlugin(), new TruncatePlugin(), new PadNumbersPlugin(), new CounterPlugin(), new DatePlugin(),
            new TmdbPlugin(metadata, fileSystem)]);
}
