using Namerre.Application.Authentication;

namespace Namerre.Authentication;

/// <summary>Reads the signed-in user from the current HTTP request, for attributing plans and runs.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public UserReference? Current => accessor.HttpContext?.User is { } user ? UserSession.Reference(user) : null;

    // Role changes replace the credential stamp and so end the session: the role claim is always current.
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole(nameof(UserRole.Admin)) == true;
}
