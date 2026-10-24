namespace Namerre.Application.Renaming.Recovery;

public sealed record RecoveryFile(string Id, string? LocatedPath, string State, IReadOnlyList<RenameIssue> Issues);
