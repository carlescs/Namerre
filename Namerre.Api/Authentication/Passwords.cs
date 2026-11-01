using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Namerre.Authentication;

/// <summary>Password hashing and the password policy shared by every account.</summary>
public static class Passwords
{
    public const int MinimumPasswordLength = 12;
    public static readonly string WeakPasswordMessage = $"Use a password of at least {MinimumPasswordLength} characters.";
    private static readonly PasswordHasher<string> Hasher = new(
        Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    // Verified for unknown users and users without a password, so a failed sign-in always costs one hash check.
    private static readonly Lazy<string> DummyHash = new(() => HashPassword(Convert.ToHexString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))));

    // Applies to new passwords only, so an existing shorter password keeps working until it is changed.
    public static bool IsAcceptable(string password)
        => password.Length >= MinimumPasswordLength && !string.IsNullOrWhiteSpace(password);

    public static string HashPassword(string password) => Hasher.HashPassword("", password);

    /// <summary>Checks a password against a hash; a null hash still costs a full check and always fails.</summary>
    public static bool Verify(string? hash, string password)
    {
        var verified = Hasher.VerifyHashedPassword("", hash ?? DummyHash.Value, password);
        return hash is not null && verified != PasswordVerificationResult.Failed;
    }

    /// <summary>Only accepts Identity V3 SHA-512 hashes with at least our configured work factor.</summary>
    public static bool IsValidHash(string? passwordHash)
    {
        try
        {
            var hash = Convert.FromBase64String(passwordHash ?? "");
            return hash.Length == 61 && hash[0] == 1 &&
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1)) == 2 &&
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5)) is >= 210_000 and <= 1_000_000 &&
                BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9)) == 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }

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
        if (!IsAcceptable(value))
            throw new InvalidOperationException($"Enter a password of at least {MinimumPasswordLength} characters.");
        Console.WriteLine(HashPassword(value));
    }
}
