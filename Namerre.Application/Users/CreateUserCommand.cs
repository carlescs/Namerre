using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Creates a user with a password, a linked single sign-on identity, or both.</summary>
public sealed record CreateUserCommand(UserReference Actor, string Username, UserRole Role, string? Password,
    ExternalIdentity? Login) : IRequest<UserRecord>;
