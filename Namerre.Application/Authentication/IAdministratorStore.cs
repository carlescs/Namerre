namespace Namerre.Application.Authentication;

public sealed record AdminCredentials(string Username, string PasswordHash);

public interface IAdministratorStore
{
    AdminCredentials? Load();
    bool TryCreate(AdminCredentials credentials);
    bool TryUpdate(AdminCredentials current, string passwordHash);
}

public sealed class AdministratorStorageException(string message, Exception innerException)
    : Exception(message, innerException);
