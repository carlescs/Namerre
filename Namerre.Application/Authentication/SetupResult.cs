namespace Namerre.Application.Authentication;

public enum SetupResult
{
    Completed,
    AlreadyConfigured,
    InvalidToken,
    WeakPassword,
    InvalidUsername
}
