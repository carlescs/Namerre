using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

public sealed record GetRenameRunsQuery(int Page, int PageSize) : IRequest<RunPage>;
