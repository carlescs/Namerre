using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Verifies the browser's answer to a started registration and registers the passkey to the actor, when they are
/// still at <see cref="ExpectedStamp"/>.
/// </summary>
public sealed record CompletePasskeyRegistrationCommand(UserReference Actor, string ExpectedStamp, string State,
    string Response, string? Name) : IRequest<UserRecord>;
