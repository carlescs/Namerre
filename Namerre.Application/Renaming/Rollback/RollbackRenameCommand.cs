using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

public sealed record RollbackRenameCommand(string Id, SelectionRequest Selection) : IRequest<JobAccepted>;
