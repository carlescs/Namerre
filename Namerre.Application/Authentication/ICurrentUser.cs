namespace Namerre.Application.Authentication;

/// <summary>The signed-in user behind the current request; null for background work such as watch folders.</summary>
public interface ICurrentUser
{
    UserReference? Current { get; }
}
