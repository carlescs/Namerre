namespace Namerre.Application.Authentication;

/// <summary>The signed-in user behind the current request; null for background work such as watch folders.</summary>
public interface ICurrentUser
{
    UserReference? Current { get; }

    /// <summary>True when the signed-in user is an administrator, who sees and manages everyone's history.</summary>
    bool IsAdmin { get; }
}
