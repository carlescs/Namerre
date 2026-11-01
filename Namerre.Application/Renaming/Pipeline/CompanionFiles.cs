namespace Namerre.Application.Renaming.Pipeline;

/// <summary>
/// Related files that follow a video: subtitles (<c>Movie.srt</c>, <c>Movie.en.hi.srt</c>), the <c>.nfo</c> and
/// artwork (<c>Movie-poster.jpg</c>, <c>-fanart</c>, <c>-thumb</c>) in the same folder, named after the video's
/// stem. The frontend mirrors these rules in <c>Namerre.Front/src/app/modules/renamer/companions.ts</c> to group
/// the collection; keep both in sync.
/// </summary>
public static class CompanionFiles
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".divx", ".flv", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts",
        ".ogm", ".ogv", ".ts", ".vob", ".webm", ".wmv"
    };

    internal static readonly HashSet<string> SubtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".tbn" };

    private static readonly string[] ArtworkKinds = ["-poster", "-fanart", "-thumb"];

    private static readonly HashSet<string> SubtitleFlags =
        new(StringComparer.OrdinalIgnoreCase) { "hi", "sdh", "cc", "forced", "default" };

    // ISO 639-1 codes; three-letter (639-2/3) codes are accepted in lower case only to limit false positives.
    private static readonly HashSet<string> Iso6391 = new(
        ("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz "
        + "ee el en eo es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik "
        + "io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml "
        + "mn mr ms mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd "
        + "se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi "
        + "vo wa wo xh yi yo za zh zu").Split(' '), StringComparer.OrdinalIgnoreCase);

    public static bool IsVideo(string name) => VideoExtensions.Contains(Path.GetExtension(name));

    /// <summary>
    /// The part of <paramref name="name"/> after <paramref name="videoStem"/> when the file is one of the video's
    /// companions (for example <c>.en.srt</c>, <c>.nfo</c> or <c>-poster.jpg</c>); otherwise null. The stem must be
    /// followed directly by a recognised suffix, so <c>Movie 2.srt</c> never belongs to <c>Movie.mkv</c>.
    /// </summary>
    public static string? Suffix(string videoStem, string name)
    {
        if (videoStem.Length == 0 || name.Length <= videoStem.Length
            || !name.StartsWith(videoStem, StringComparison.Ordinal))
            return null;
        var rest = name[videoStem.Length..];
        if (rest.Equals(".nfo", StringComparison.OrdinalIgnoreCase)) return rest;
        var extension = Path.GetExtension(rest);
        var middle = rest[..^extension.Length];
        if (ImageExtensions.Contains(extension)
            && ArtworkKinds.Any(kind => middle.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            return rest;
        if (!SubtitleExtensions.Contains(extension)) return null;
        if (middle.Length == 0) return rest;
        return middle[0] == '.' && middle[1..].Split('.').All(IsSubtitleToken) ? rest : null;
    }

    /// <summary>
    /// Pairs each companion in <paramref name="files"/> with a video from the same request and source folder,
    /// preferring the longest matching video stem. Videos never become companions.
    /// </summary>
    public static IReadOnlyDictionary<string, Companion> Detect(IReadOnlyList<FileInput> files)
    {
        var videos = files.Where(f => f.Path is not null && IsVideo(f.Path))
            .Select(f => (File: f, Folder: Folder(f.Path), Stem: Path.GetFileNameWithoutExtension(Name(f.Path))))
            .ToArray();
        var result = new Dictionary<string, Companion>(StringComparer.Ordinal);
        if (videos.Length == 0) return result;
        foreach (var file in files)
        {
            if (file.Path is null || IsVideo(file.Path)) continue;
            var folder = Folder(file.Path);
            var name = Name(file.Path);
            Companion? best = null;
            var bestLength = -1;
            foreach (var video in videos)
            {
                if (video.Folder != folder || video.Stem.Length <= bestLength) continue;
                var suffix = Suffix(video.Stem, name);
                if (suffix is null) continue;
                best = new(video.File.Id, suffix);
                bestLength = video.Stem.Length;
            }
            if (best is not null) result[file.Id] = best;
        }
        return result;
    }

    /// <summary>
    /// Trailing language and flag tokens of a subtitle stem (for example <c>.en.hi</c> in
    /// <c>Episode.en.hi.srt</c>), which belong to the compound extension. The first token is the name itself.
    /// </summary>
    internal static string SubtitleSuffix(string stem, string extension)
    {
        if (!SubtitleExtensions.Contains(extension)) return "";
        var tokens = stem.Split('.');
        var start = tokens.Length;
        while (start > 1 && IsSubtitleToken(tokens[start - 1])) start--;
        return start == tokens.Length ? "" : "." + string.Join('.', tokens[start..]);
    }

    internal static bool IsSubtitleToken(string token)
    {
        if (SubtitleFlags.Contains(token)) return true;
        var separator = token.IndexOfAny(['-', '_']);
        var language = separator < 0 ? token : token[..separator];
        if (separator >= 0)
        {
            var region = token[(separator + 1)..];
            var validRegion = region.Length == 2 && region.All(char.IsAsciiLetter)
                || region.Length == 4 && region.All(char.IsAsciiLetter);
            if (!validRegion) return false;
        }
        return language.Length == 2 ? Iso6391.Contains(language)
            : language.Length == 3 && language.All(char.IsAsciiLetterLower);
    }

    private static string Folder(string path) => path[..(path.LastIndexOf('/') + 1)];

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
}
