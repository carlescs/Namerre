using System.Text.RegularExpressions;
using Namerre.Application.WatchFolders;
using Namerre.Domain.Renaming;

namespace Namerre.Infrastructure.Configuration;

/// <summary>One entry of <c>Namerre:WatchFolders</c>; see <see cref="WatchFolderSettings"/>. Invalid entries fail startup.</summary>
public sealed partial class WatchFolderOptions
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Preset { get; set; }
    public bool Recursive { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public int StableSeconds { get; set; } = 60;
    public bool IncludeCompanions { get; set; } = true;
    public string? TmdbMediaType { get; set; }
    public double? TmdbAutoConfirm { get; set; }
    public int MaxAttempts { get; set; } = 5;

    public WatchFolderSettings ToSettings() => new(Name!, Path!.Trim('/'), Preset!,
        TimeSpan.FromSeconds(IntervalSeconds), TimeSpan.FromSeconds(StableSeconds), Recursive, IncludeCompanions,
        TmdbMediaType, TmdbAutoConfirm, MaxAttempts);

    /// <summary>The first problem of the list, or null when every folder is valid.</summary>
    public static string? Problem(IReadOnlyList<WatchFolderOptions> folders)
    {
        foreach (var folder in folders)
        {
            var name = folder.Name ?? "";
            if (!NamePattern().IsMatch(name))
                return "Namerre:WatchFolders names are 1–50 letters, digits, spaces, '-' or '_'.";
            if (folders.Count(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) > 1)
                return $"Watch folder name {name} is used more than once.";
            try { NamingRules.ValidateRelativePath(folder.Path?.Trim('/') ?? ""); }
            catch (ArgumentException) { return $"Watch folder {name} needs a root-relative Path with forward slashes."; }
            if (string.IsNullOrWhiteSpace(folder.Preset)) return $"Watch folder {name} needs the name of a Preset.";
            if (folder.IntervalSeconds is < 10 or > 86400) return $"Watch folder {name}: IntervalSeconds is 10–86400.";
            if (folder.StableSeconds is < 0 or > 86400) return $"Watch folder {name}: StableSeconds is 0–86400.";
            if (folder.MaxAttempts is < 1 or > 20) return $"Watch folder {name}: MaxAttempts is 1–20.";
            if (folder.TmdbMediaType is not (null or "movie" or "tv"))
                return $"Watch folder {name}: TmdbMediaType is movie or tv.";
            if (folder.TmdbAutoConfirm is { } threshold && (threshold is < 0.5 or > 1 || folder.TmdbMediaType is null))
                return $"Watch folder {name}: TmdbAutoConfirm is 0.5–1 and needs TmdbMediaType.";
        }
        // Overlapping folders would offer the same file to two presets.
        foreach (var folder in folders)
        foreach (var other in folders.Where(other => !ReferenceEquals(other, folder)))
        {
            var path = folder.Path!.Trim('/');
            var inner = other.Path!.Trim('/');
            if (string.Equals(path, inner, StringComparison.OrdinalIgnoreCase)
                || (folder.Recursive && inner.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                return $"Watch folders {folder.Name} and {other.Name} overlap.";
        }
        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9 _-]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
