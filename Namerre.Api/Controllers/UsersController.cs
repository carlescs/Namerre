using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.Authentication;
using Namerre.Authentication;

namespace Namerre.Controllers;

/// <summary>User management for administrators: accounts, roles, passwords and single sign-on links.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthenticationSetup.AdminPolicy)]
[TypeFilter(typeof(AccountExceptionFilter))]
public sealed class UsersController(UserAccounts accounts, OidcLogin oidc) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(accounts.List().Select(UserView.From));

    [HttpGet("{id:long}")]
    public IActionResult Get(long id) => Ok(UserView.From(accounts.Find(id) ?? throw AccountException.NotFound()));

    [HttpPost]
    public IActionResult Create(CreateUserRequest request)
    {
        var user = accounts.Create(Actor, request.Username, request.Role ?? UserRole.User, request.Password,
            Identity(request.Subject, request.DisplayName));
        return Created($"/api/users/{user.Id}", UserView.From(user));
    }

    [HttpPatch("{id:long}")]
    public IActionResult Update(long id, UpdateUserRequest request)
        => Ok(UserView.From(accounts.Update(Actor, id, request.Role, request.Disabled)));

    [HttpPut("{id:long}/password")]
    public IActionResult SetPassword(long id, SetPasswordRequest request)
        => Ok(UserView.From(accounts.SetPassword(Actor, id, null, request.Password, recentSignIn: false)));

    [HttpDelete("{id:long}/password")]
    public IActionResult RemovePassword(long id) => Ok(UserView.From(accounts.RemovePassword(Actor, id)));

    /// <summary>
    /// Links a single sign-on identity: a new one by its subject, or one already linked to another user by its
    /// <see cref="LinkRequest.LoginId"/> (moving it, and optionally deleting the user it leaves).
    /// </summary>
    [HttpPost("{id:long}/logins")]
    public IActionResult Link(long id, LinkRequest request)
    {
        if (request.LoginId is { } loginId)
            return Ok(UserView.From(accounts.MoveLogin(Actor, loginId, id, request.DeleteSource)));
        return Ok(UserView.From(accounts.Link(Actor, id, Identity(request.Subject, request.DisplayName)
            ?? throw new AccountException("InvalidLink", "Enter the identity's subject or choose a linked identity."))));
    }

    [HttpDelete("{id:long}/logins/{loginId:long}")]
    public IActionResult Unlink(long id, long loginId) => Ok(UserView.From(accounts.Unlink(Actor, id, loginId)));

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        accounts.Delete(Actor, id);
        return NoContent();
    }

    private UserReference Actor => UserSession.Reference(User) ?? throw AccountException.NotFound();

    private ExternalIdentity? Identity(string? subject, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;
        if (!oidc.Enabled)
            throw new AccountException("SingleSignOnDisabled", "Single sign-on is not configured.", 409);
        subject = subject.Trim();
        return new ExternalIdentity(oidc.Issuer, subject,
            string.IsNullOrWhiteSpace(displayName) ? subject : displayName.Trim());
    }
}

public sealed record UserView(long Id, string Username, string Role, bool Disabled, bool HasPassword,
    bool ManagedByConfiguration, DateTimeOffset? CreatedAt, DateTimeOffset? LastSignInAt,
    IReadOnlyList<LoginView> Logins)
{
    public static UserView From(UserRecord user) => new(user.Id, user.Username, user.Role.ToString(), user.Disabled,
        user.HasPassword, user.Id == UserAccounts.ConfiguredUserId,
        user.Id == UserAccounts.ConfiguredUserId ? null : user.CreatedAt, user.LastSignInAt,
        user.Logins.Select(LoginView.From).ToArray());
}

/// <param name="Password">Optional when <paramref name="Subject"/> links a single sign-on identity instead.</param>
/// <param name="Subject">The <c>sub</c> claim of a single sign-on identity to link right away.</param>
public sealed record CreateUserRequest(
    [Required, StringLength(128)] string Username,
    UserRole? Role,
    [StringLength(1024)] string? Password,
    [StringLength(256)] string? Subject,
    [StringLength(256)] string? DisplayName);

public sealed record UpdateUserRequest(UserRole? Role, bool? Disabled);

public sealed record SetPasswordRequest([Required, StringLength(1024)] string Password);

public sealed record LinkRequest(
    [StringLength(256)] string? Subject,
    [StringLength(256)] string? DisplayName,
    long? LoginId,
    bool DeleteSource = false);
