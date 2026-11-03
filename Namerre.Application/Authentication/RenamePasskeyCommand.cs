using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Renames a passkey of a user; a label only, so sessions continue.</summary>
public sealed record RenamePasskeyCommand(UserReference Actor, long UserId, long PasskeyId, string Name)
    : IRequest<UserRecord>;
