using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Creates the first administrator with the one-time installation token printed to the console.</summary>
public sealed record CompleteSetupCommand(string Token, string Username, string Password) : IRequest<SetupResult>;
