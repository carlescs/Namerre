namespace Namerre.Application.Authentication;

/// <summary>Who did something, as recorded on plans and runs. The username is kept so history survives deletion.</summary>
public sealed record UserReference(long Id, string Username);
