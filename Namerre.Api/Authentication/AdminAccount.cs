using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Authentication;

public enum SetupResult
{
    Completed,
    AlreadyConfigured,
    InvalidToken,
    WeakPassword
}

public enum PasswordChangeResult
{
    Changed,
    InvalidPassword,
    ManagedByConfiguration,
    Conflict,
    WeakPassword
}

public sealed class AdminAccount
{
    public const string StampClaim = "credential-stamp";
    public const int MinimumPasswordLength = 12;
    private static readonly PasswordHasher<string> Hasher = new(
        Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly object gate = new();
    private readonly IAdministratorStore store;
    private readonly bool configured;
    private AdminCredentials? credentials;

    public AdminAccount(IOptions<NamerreOptions> options, IAdministratorStore store)
    {
        this.store = store;
        var username = options.Value.Authentication.Username;
        var passwordHash = options.Value.Authentication.PasswordHash;
        configured = !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(passwordHash);
        credentials = configured
            ? new AdminCredentials(username ?? "", passwordHash ?? "")
            : store.Load();
        if (credentials is not null)
        {
            Validate(credentials);
            return;
        }

        SetupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Console.Error.WriteLine($"Namerre installation: open the app over HTTPS and enter this one-time setup token: {SetupToken}");
    }

    // Server-configured credentials can only be changed in configuration.
    public bool CanChangePassword => !configured && !RequiresSetup;
    public bool RequiresSetup => Volatile.Read(ref credentials) is null;
    public string Username => Volatile.Read(ref credentials)?.Username ?? "";
    public string Stamp => StampOf(Volatile.Read(ref credentials));
    internal string? SetupToken { get; private set; }

    public SetupResult CompleteSetup(string token, string username, string password)
    {
        lock (gate)
        {
            if (credentials is not null)
                return SetupResult.AlreadyConfigured;
            if (SetupToken is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(SetupToken), Encoding.UTF8.GetBytes(token)))
                return SetupResult.InvalidToken;
            if (!IsAcceptablePassword(password))
                return SetupResult.WeakPassword;
            var account = new AdminCredentials(username, HashPassword(password));
            Validate(account);
            if (!store.TryCreate(account))
                return SetupResult.AlreadyConfigured;
            Volatile.Write(ref credentials, account);
            SetupToken = null;
            return SetupResult.Completed;
        }
    }

    public static string StampOf(AdminCredentials? account) => account is null ? "" : Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(account.Username + "\0" + account.PasswordHash)));

    // Returns the credentials that changed so callers sign in with exactly that stamp.
    public PasswordChangeResult ChangePassword(string currentPassword, string newPassword,
        out AdminCredentials? changed)
    {
        changed = null;
        lock (gate)
        {
            if (configured || credentials is null)
                return PasswordChangeResult.ManagedByConfiguration;
            var account = credentials;
            if (Hasher.VerifyHashedPassword(account.Username, account.PasswordHash, currentPassword) ==
                PasswordVerificationResult.Failed)
                return PasswordChangeResult.InvalidPassword;
            if (!IsAcceptablePassword(newPassword))
                return PasswordChangeResult.WeakPassword;
            var updated = account with { PasswordHash = HashPassword(newPassword) };
            if (!store.TryUpdate(account, updated.PasswordHash))
                return PasswordChangeResult.Conflict;
            Volatile.Write(ref credentials, updated);
            changed = updated;
            return PasswordChangeResult.Changed;
        }
    }

    private static void Validate(AdminCredentials account)
    {
        try
        {
            var hash = Convert.FromBase64String(account.PasswordHash ?? "");
            // Only accept Identity V3 SHA-512 hashes with at least our configured work factor.
            if (string.IsNullOrWhiteSpace(account.Username) || account.Username.Length > 128 ||
                hash.Length != 61 || hash[0] != 1 ||
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1)) != 2 ||
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5)) is < 210_000 or > 1_000_000 ||
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9)) != 16)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "The administrator credentials are invalid. Configure Namerre:Authentication:Username and Namerre:Authentication:PasswordHash, or restore valid administrator data.");
        }
    }

    public bool Verify(string username, string password) => Authenticate(username, password) is not null;

    // Returns the verified credentials snapshot, so a concurrent password change cannot hand an
    // old-password login the new stamp.
    public AdminCredentials? Authenticate(string username, string password)
    {
        var account = Volatile.Read(ref credentials);
        if (account is null)
            return null;
        // Always perform the password hash check, even for an unknown username.
        var verified = Hasher.VerifyHashedPassword(account.Username, account.PasswordHash, password);
        return verified != PasswordVerificationResult.Failed &&
            string.Equals(username, account.Username, StringComparison.Ordinal) ? account : null;
    }

    // Applies to new passwords only, so an existing shorter password keeps working until it is changed.
    public static bool IsAcceptablePassword(string password)
        => password.Length >= MinimumPasswordLength && !string.IsNullOrWhiteSpace(password);

    public static string HashPassword(string password) => Hasher.HashPassword("", password);

    public static void GeneratePasswordHash()
    {
        Console.Error.Write("Administrator password: ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
            }
            else if (!char.IsControl(key.KeyChar) && password.Length < 1024)
                password.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        var value = password.ToString();
        if (!IsAcceptablePassword(value))
            throw new InvalidOperationException($"Enter a password of at least {MinimumPasswordLength} characters.");
        Console.WriteLine(HashPassword(value));
    }
}
