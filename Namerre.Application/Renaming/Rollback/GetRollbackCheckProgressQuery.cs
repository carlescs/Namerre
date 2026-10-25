using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

public sealed record GetRollbackCheckProgressQuery(string Id, string OperationId) : IRequest<RollbackCheckProgress>;
