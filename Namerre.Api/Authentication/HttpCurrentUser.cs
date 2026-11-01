using Namerre.Application.Authentication;

namespace Namerre.Authentication;

/// <summary>Reads the signed-in user from the current HTTP request, for attributing plans and runs.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public UserReference? Current => accessor.HttpContext?.User is { } user ? UserSession.Reference(user) : null;
}
