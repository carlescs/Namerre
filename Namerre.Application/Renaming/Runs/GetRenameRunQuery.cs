using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

public sealed record GetRenameRunQuery(string Id) : IRequest<RunView>;
