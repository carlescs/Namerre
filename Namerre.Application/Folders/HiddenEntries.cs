namespace Namerre.Application.Folders;

/// <summary>
/// Names that operating systems and NAS software create next to media: dotfiles (.DS_Store),
/// Synology metadata (@eaDir) and the usual Windows and recycle-bin entries. Keep in sync with
/// <c>Namerre.Front/src/app/modules/renamer/hidden-entries.ts</c>.
/// </summary>
public static class HiddenEntries
{
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "#recycle",
        "#snapshot",
        "$recycle.bin",
        "desktop.ini",
        "lost+found",
        "system volume information",
        "thumbs.db",
    };

    public static bool IsHidden(string name) =>
        name.StartsWith('.') || name.StartsWith('@') || SystemNames.Contains(name);
}
