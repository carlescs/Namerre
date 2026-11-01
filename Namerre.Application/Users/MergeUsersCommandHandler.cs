using CMediator.Abstractions;
using Microsoft.Extensions.Logging;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class MergeUsersCommandHandler(UserAccounts accounts, IUserHistory history,
    ILogger<MergeUsersCommandHandler> logger) : IRequestHandler<MergeUsersCommand, UserRecord>
{
    public async Task<UserRecord> Handle(MergeUsersCommand request, CancellationToken cancellationToken)
    {
        var source = accounts.Find(request.SourceUserId) ?? throw AccountException.NotFound();
        var merged = accounts.Merge(request.Actor, source.Id, request.TargetUserId);
        try
        {
            // The users are merged already; history keeps the source's name if this fails.
            await history.Reattribute(source.Id, new UserReference(merged.Id, merged.Username), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to attribute the history of merged user {SourceId} to user {UserId}.",
                source.Id, merged.Id);
            throw new AccountException("HistoryNotMerged",
                $"{source.Username} was merged into {merged.Username}, but their run history still shows {source.Username}.",
                503);
        }
        return merged;
    }
}
