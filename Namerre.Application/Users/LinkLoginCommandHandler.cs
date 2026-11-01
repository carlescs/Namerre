using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class LinkLoginCommandHandler(UserAccounts accounts) : IRequestHandler<LinkLoginCommand, UserRecord>
{
    public Task<UserRecord> Handle(LinkLoginCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Link(request.Actor, request.UserId, request.Identity));
}
