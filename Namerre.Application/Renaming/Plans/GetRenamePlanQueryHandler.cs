using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

internal sealed class GetRenamePlanQueryHandler(IRenameService service)
    : IRequestHandler<GetRenamePlanQuery, PlanView>
{
    public Task<PlanView> Handle(GetRenamePlanQuery request, CancellationToken cancellationToken)
        => service.GetPlan(request.Id, cancellationToken);
}
