using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

public sealed record DeleteRenameRunsCommand(DeleteRunsRequest Request) : IRequest<IReadOnlyList<string>>;
