using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Deletes a user; returns it as it was.</summary>
public sealed record DeleteUserCommand(UserReference Actor, long UserId) : IRequest<UserRecord>;
