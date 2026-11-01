using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>True when <paramref name="Password"/> is the user's password, e.g. to confirm a sensitive change.</summary>
public sealed record VerifyPasswordQuery(long UserId, string? Password) : IRequest<bool>;
