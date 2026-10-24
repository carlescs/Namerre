namespace Namerre.Application.Renaming.Runs;

public sealed record RunPage(IReadOnlyList<RunView> Items, int Page, int PageSize, bool HasMore);
