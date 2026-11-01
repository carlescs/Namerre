using System.Text.Json;
using System.Text.Json.Nodes;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

/// <summary>
/// Logic the frontend reimplements (companions, options hash, file name parsing, template rendering, confidence
/// threshold) is checked against the same language-neutral input/output vectors as the Vitest specs, which load
/// <c>Namerre.Front/src/app/modules/renamer/test-vectors/*.json</c>. Add a case there to cover both sides.
/// </summary>
public class MirroredLogicTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static JsonNode Vectors(string file)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", file)))!;

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    public static TheoryData<string, bool> VideoCases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var c in Vectors("companions.json")["isVideo"]!.AsArray())
            data.Add(Text(c!["name"])!, c["expected"]!.GetValue<bool>());
        return data;
    }

    [Theory]
    [MemberData(nameof(VideoCases))]
    public void IsVideo_matches_the_shared_vectors(string name, bool expected)
        => Assert.Equal(expected, CompanionFiles.IsVideo(name));

    public static TheoryData<string, string, string?> SuffixCases()
    {
        var data = new TheoryData<string, string, string?>();
        foreach (var c in Vectors("companions.json")["suffix"]!.AsArray())
            data.Add(Text(c!["videoStem"])!, Text(c["name"])!, Text(c["expected"]));
        return data;
    }

    [Theory]
    [MemberData(nameof(SuffixCases))]
    public void Suffix_matches_the_shared_vectors(string videoStem, string name, string? expected)
        => Assert.Equal(expected, CompanionFiles.Suffix(videoStem, name));

    public static TheoryData<int> DetectCases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors("companions.json")["detect"]!.AsArray().Count; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(DetectCases))]
    public void Detect_matches_the_shared_vectors(int index)
    {
        var vector = Vectors("companions.json")["detect"]![index]!;
        var paths = vector["files"]!.AsArray().Select(Text).ToArray();
        var expected = vector["expected"]!.AsObject().ToDictionary(p => p.Key, p => Text(p.Value)!);

        var detected = CompanionFiles.Detect(paths.Select(path => new FileInput(path!, path!)).ToArray());

        // Ids are the paths, so a companion's video id is the video's path.
        Assert.Equal(expected.OrderBy(p => p.Key), detected.ToDictionary(p => p.Key, p => p.Value.VideoId).OrderBy(p => p.Key));
    }

    public static TheoryData<int> OptionsHashCases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors("options-hash.json").AsArray().Count; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(OptionsHashCases))]
    public void OptionsHash_matches_the_shared_vectors(int index)
    {
        var vector = Vectors("options-hash.json")[index]!;
        var options = JsonDocument.Parse(vector["options"]!.ToJsonString()).RootElement;
        Assert.Equal(Text(vector["expected"]), TmdbPlugin.OptionsHash(options));
    }

    public static TheoryData<int> FileNameCases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors("tmdb-filename.json").AsArray().Count; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(FileNameCases))]
    public void TmdbFileName_matches_the_shared_vectors(int index)
    {
        var vector = Vectors("tmdb-filename.json")[index]!;
        var expected = vector["expected"]!;

        var parsed = TmdbFileName.Parse(Text(vector["path"])!, Text(vector["mediaType"])!);

        Assert.Equal(
            new ParsedFileName(Text(expected["query"])!, Text(expected["year"]), expected["season"]?.GetValue<int>(),
                expected["episode"]?.GetValue<int>(), expected["episodeEnd"]?.GetValue<int>()),
            parsed);
    }

    public static TheoryData<int> TemplateCases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors("tmdb-template.json")["cases"]!.AsArray().Count; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public void TmdbPlugin_Render_matches_the_shared_vectors(int index)
    {
        var vectors = Vectors("tmdb-template.json");
        var vector = vectors["cases"]![index]!;
        var match = vectors["matches"]![Text(vector["match"])!]!.Deserialize<TmdbMatch>(Json)!;
        var template = vector["template"] is { } text ? Text(text)! : Repeat(vector["templateRepeat"]!);
        var range = Text(vector["episodeRange"]) ?? TmdbPlugin.RangeRepeat;

        if (vector["error"]?.GetValue<bool>() == true)
        {
            Assert.Throws<RenameException>(() => TmdbPlugin.Render(template, match, range));
            return;
        }
        var expected = vector["expected"] is { } value ? Text(value) : Repeat(vector["expectedRepeat"]!);
        Assert.Equal(expected, TmdbPlugin.Render(template, match, range));
    }

    private static string Repeat(JsonNode node) => string.Concat(Enumerable.Repeat(Text(node["text"])!, node["count"]!.GetValue<int>()));

    [Fact]
    public void High_confidence_threshold_matches_the_shared_vectors()
    {
        var vectors = Vectors("confidence.json");
        Assert.Equal(MatchConfidence.High, vectors["high"]!.GetValue<double>());
        foreach (var c in vectors["cases"]!.AsArray())
            Assert.Equal(c!["isHigh"]!.GetValue<bool>(), c["score"]!.GetValue<double>() >= MatchConfidence.High);
    }
}
