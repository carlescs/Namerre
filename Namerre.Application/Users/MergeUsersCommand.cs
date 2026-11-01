using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

/// <summary>
/// Merges one user into another: every identity moves to the target, the source's history is attributed to the
/// target and the source is deleted with its password. The target keeps its own role and password.
/// </summary>
public sealed record MergeUsersCommand(UserReference Actor, long SourceUserId, long TargetUserId) : IRequest<UserRecord>;
