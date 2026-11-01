namespace Namerre.Application.Authentication;

/// <summary>Hashes and checks passwords. Hashing is deliberately slow.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Checks a password against a hash; a null hash still costs a full check and always fails.</summary>
    bool Verify(string? hash, string password);

    /// <summary>True when <paramref name="hash"/> has the format and work factor this hasher accepts.</summary>
    bool IsValidHash(string? hash);
}
