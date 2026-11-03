using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class SessionEpochQueryHandler(UserSessions sessions) : IRequestHandler<SessionEpochQuery, long>
{
    public Task<long> Handle(SessionEpochQuery request, CancellationToken cancellationToken)
        => Task.FromResult(sessions.CurrentEpoch(request.UserId));
}
