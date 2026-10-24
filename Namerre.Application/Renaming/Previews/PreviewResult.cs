namespace Namerre.Application.Renaming.Previews;

public sealed record PreviewResult(IReadOnlyList<PreviewFile> Files, bool CanPrepare);
