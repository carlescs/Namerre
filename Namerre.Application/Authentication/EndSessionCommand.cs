using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Signs a session out on the server, so a copy of its cookie stops working. Returns true.</summary>
public sealed record EndSessionCommand(UserReference User, string SessionId) : IRequest<bool>;
