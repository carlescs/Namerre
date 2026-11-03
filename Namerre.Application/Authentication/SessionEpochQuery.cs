using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>The session epoch a new session of the user is issued with.</summary>
public sealed record SessionEpochQuery(long UserId) : IRequest<long>;
