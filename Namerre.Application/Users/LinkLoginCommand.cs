using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Links a single sign-on identity that is not linked to anyone yet.</summary>
public sealed record LinkLoginCommand(UserReference Actor, long UserId, ExternalIdentity Identity) : IRequest<UserRecord>;
