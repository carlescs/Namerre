using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

public sealed record GetDeletableRenameRunsQuery : IRequest<DeletableRuns>;
