using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>True while no administrator exists yet and the installation token must create the first one.</summary>
public sealed record RequiresSetupQuery : IRequest<bool>;
