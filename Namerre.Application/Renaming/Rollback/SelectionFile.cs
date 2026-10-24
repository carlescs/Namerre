namespace Namerre.Application.Renaming.Rollback;

public sealed record SelectionFile(string Id, bool Safe, string CurrentPath, string DestinationPath,
    IReadOnlyList<RenameIssue> Issues);
