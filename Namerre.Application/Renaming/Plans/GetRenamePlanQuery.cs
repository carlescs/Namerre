using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

public sealed record GetRenamePlanQuery(string Id) : IRequest<PlanView>;
