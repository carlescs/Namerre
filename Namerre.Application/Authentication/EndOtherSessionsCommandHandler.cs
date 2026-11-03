using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class EndOtherSessionsCommandHandler(UserSessions sessions)
    : IRequestHandler<EndOtherSessionsCommand, long>
{
    public Task<long> Handle(EndOtherSessionsCommand request, CancellationToken cancellationToken)
        => Task.FromResult(sessions.EndAll(request.User));
}
