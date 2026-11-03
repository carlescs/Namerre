using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class EndSessionCommandHandler(UserSessions sessions) : IRequestHandler<EndSessionCommand, bool>
{
    public Task<bool> Handle(EndSessionCommand request, CancellationToken cancellationToken)
    {
        sessions.End(request.User, request.SessionId);
        return Task.FromResult(true);
    }
}
