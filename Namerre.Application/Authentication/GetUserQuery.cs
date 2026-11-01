using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>A user by ID, including the configured administrator; null when there is none.</summary>
public sealed record GetUserQuery(long Id) : IRequest<UserRecord?>;
