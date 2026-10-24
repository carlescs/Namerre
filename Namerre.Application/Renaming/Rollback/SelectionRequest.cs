namespace Namerre.Application.Renaming.Rollback;

public sealed record SelectionRequest(IReadOnlyList<string> FileIds, string OperationId);
