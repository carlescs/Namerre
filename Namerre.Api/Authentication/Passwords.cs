using System.Text;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Authentication;

namespace Namerre.Authentication;

/// <summary>The <c>--hash-password</c> command, which hashes a password for <c>Namerre:Authentication</c>.</summary>
public static class Passwords
{
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
        if (!PasswordPolicy.IsAcceptable(value))
            throw new InvalidOperationException($"Enter a password of at least {PasswordPolicy.MinimumLength} characters.");
        Console.WriteLine(new PasswordHasher().Hash(value));
    }
}
