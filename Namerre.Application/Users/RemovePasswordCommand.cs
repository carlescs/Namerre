using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Removes a user's password, when they have another way to sign in or are disabled.</summary>
public sealed record RemovePasswordCommand(UserReference Actor, long UserId) : IRequest<UserRecord>;
