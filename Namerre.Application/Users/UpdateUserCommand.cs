using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Changes a user's role or disabled flag; either ends the user's sessions.</summary>
public sealed record UpdateUserCommand(UserReference Actor, long UserId, UserRole? Role, bool? Disabled) : IRequest<UserRecord>;
