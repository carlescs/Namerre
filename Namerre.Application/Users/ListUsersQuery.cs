using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>Every user, the configured administrator first.</summary>
public sealed record ListUsersQuery : IRequest<IReadOnlyList<UserRecord>>;
