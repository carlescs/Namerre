using System.ComponentModel.DataAnnotations;
using CMediator.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namerre.Application.Authentication;
using Namerre.Application.Users;
using Namerre.Authentication;

namespace Namerre.Controllers;

/// <summary>User management for administrators: accounts, roles, passwords and single sign-on links.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthenticationSetup.AdminPolicy)]
[TypeFilter(typeof(AccountExceptionFilter))]
public sealed class UsersController(ISender sender, OidcLogin oidc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok((await sender.Send(new ListUsersQuery(), cancellationToken)).Select(UserView.From));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new GetUserQuery(id), cancellationToken)
            ?? throw AccountException.NotFound()));

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await sender.Send(new CreateUserCommand(Actor, request.Username, request.Role ?? UserRole.User,
            request.Password, Identity(request.Subject, request.DisplayName)), cancellationToken);
        return Created($"/api/users/{user.Id}", UserView.From(user));
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, UpdateUserRequest request, CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new UpdateUserCommand(Actor, id, request.Role, request.Disabled),
            cancellationToken)));

    [HttpPut("{id:long}/password")]
    public async Task<IActionResult> SetPassword(long id, SetPasswordRequest request,
        CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new SetPasswordCommand(Actor, id, null, request.Password),
            cancellationToken)));

    [HttpDelete("{id:long}/password")]
    public async Task<IActionResult> RemovePassword(long id, CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new RemovePasswordCommand(Actor, id), cancellationToken)));

    /// <summary>
    /// Links a single sign-on identity: a new one by its subject, or one already linked to another user by its
    /// <see cref="LinkRequest.LoginId"/>, keeping or deleting (<see cref="LinkRequest.DeleteSource"/>) the user it
    /// leaves. To take over that user entirely, use <see cref="Merge"/>.
    /// </summary>
    [HttpPost("{id:long}/logins")]
    public async Task<IActionResult> Link(long id, LinkRequest request, CancellationToken cancellationToken)
    {
        if (request.LoginId is { } moved)
            return Ok(UserView.From(await sender.Send(new MoveLoginCommand(Actor, moved, id, request.DeleteSource),
                cancellationToken)));
        var identity = Identity(request.Subject, request.DisplayName)
            ?? throw new AccountException("InvalidLink", "Enter the identity's subject or choose a linked identity.");
        return Ok(UserView.From(await sender.Send(new LinkLoginCommand(Actor, id, identity), cancellationToken)));
    }

    /// <summary>
    /// Merges another user into this one, e.g. a user created automatically on first sign-in into the person's
    /// existing user: all its identities move here, its plans and runs are attributed to this user, and it is
    /// deleted with its password. This user keeps its role and password.
    /// </summary>
    [HttpPost("{id:long}/merge")]
    public async Task<IActionResult> Merge(long id, MergeUserRequest request, CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new MergeUsersCommand(Actor, request.SourceUserId, id),
            cancellationToken)));

    [HttpDelete("{id:long}/logins/{loginId:long}")]
    public async Task<IActionResult> Unlink(long id, long loginId, CancellationToken cancellationToken)
        => Ok(UserView.From(await sender.Send(new UnlinkLoginCommand(Actor, id, loginId), cancellationToken)));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteUserCommand(Actor, id), cancellationToken);
        return NoContent();
    }

    private UserReference Actor => UserSession.Reference(User) ?? throw AccountException.NotFound();

    /// <summary>An identity of the configured provider; its issuer comes from the server's settings.</summary>
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

/// <param name="SourceUserId">The user merged into the target and then deleted.</param>
public sealed record MergeUserRequest(long SourceUserId);
