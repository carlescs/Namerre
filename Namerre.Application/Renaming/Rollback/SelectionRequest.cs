namespace Namerre.Application.Renaming.Rollback;

/// <param name="DeleteEmptyFolders">
/// Rollback only: remove folders the undo leaves empty. Null inherits the original run's choice.
/// </param>
public sealed record SelectionRequest(IReadOnlyList<string> FileIds, string OperationId, bool? DeleteEmptyFolders = null);
