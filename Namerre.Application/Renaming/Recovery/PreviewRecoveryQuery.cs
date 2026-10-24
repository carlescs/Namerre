using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Recovery;

public sealed record PreviewRecoveryQuery(string Id) : IRequest<RecoveryPreview>;
