using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Recovery;

public sealed record RecoverRenameCommand(string Id, RecoveryRequest Request) : IRequest<JobAccepted>;
