using Namerre.Application.Authentication;

namespace Namerre.Testing;

/// <summary>A fast, transparent <see cref="IPasswordHasher"/>; the real one is deliberately slow.</summary>
internal sealed class TestPasswordHasher : IPasswordHasher
{
    private const string Prefix = "test-hash:";
    public static readonly TestPasswordHasher Instance = new();

    public string Hash(string password) => Prefix + password;

    public bool Verify(string? hash, string password) => hash is not null && hash == Hash(password);

    public bool IsValidHash(string? hash) => hash?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}
