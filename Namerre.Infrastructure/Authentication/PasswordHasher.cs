using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;

namespace Namerre.Infrastructure.Authentication;

/// <summary>ASP.NET Core Identity V3 password hashes (PBKDF2 with HMAC-SHA512) with a fixed work factor.</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    public const int IterationCount = 210_000;
    private static readonly PasswordHasher<string> Hasher = new(
        Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));
    // Verified for unknown users and users without a password, so a failed sign-in always costs one hash check.
    private static readonly Lazy<string> DummyHash = new(() => Hasher.HashPassword("",
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));

    public string Hash(string password) => Hasher.HashPassword("", password);

    public bool Verify(string? hash, string password)
    {
        var verified = Hasher.VerifyHashedPassword("", hash ?? DummyHash.Value, password);
        return hash is not null && verified != PasswordVerificationResult.Failed;
    }

    /// <summary>Only accepts Identity V3 SHA-512 hashes with at least our configured work factor.</summary>
    public bool IsValidHash(string? hash)
    {
        try
        {
            var bytes = Convert.FromBase64String(hash ?? "");
            return bytes.Length == 61 && bytes[0] == 1 &&
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)) == 2 &&
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)) is >= IterationCount and <= 1_000_000 &&
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9)) == 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
