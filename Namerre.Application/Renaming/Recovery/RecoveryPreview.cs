namespace Namerre.Application.Renaming.Recovery;

public sealed record RecoveryPreview(string RunId, bool CanRecover, string Action, IReadOnlyList<RecoveryFile> Files);
