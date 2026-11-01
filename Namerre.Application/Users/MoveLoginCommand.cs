using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Moves a linked identity to another user, keeping or deleting the user it leaves.</summary>
public sealed record MoveLoginCommand(UserReference Actor, long LoginId, long ToUserId, bool DeleteSource) : IRequest<UserRecord>;
