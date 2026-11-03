using Microsoft.AspNetCore.Mvc.Filters;
using Namerre.Application.Authentication;
using Namerre.Authentication;

namespace Namerre.Controllers;

/// <summary>
/// Lets safe requests through and refuses every other request of the controller it is applied to unless the session
/// signed in recently (<see cref="UserSession.IsRecent"/>), answering exactly like a refused own-account change
/// (<see cref="AccountException.RecentSignIn"/>). Applied to a whole controller it also covers endpoints added later.
/// </summary>
public sealed class RecentSignInFilter(TimeProvider time) : IAuthorizationFilter, IOrderedFilter
{
    /// <summary>Runs after <c>[Authorize]</c>, so unauthenticated or non-admin callers keep their 401 or 403.</summary>
    public int Order => 10;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method) || HttpMethods.IsTrace(http.Request.Method)
            || http.User.Identity?.IsAuthenticated != true
            || UserSession.IsRecent(http.User, time.GetUtcNow()))
            return;
        context.Result = AccountExceptionFilter.Result(AccountException.RecentSignIn());
    }
}
