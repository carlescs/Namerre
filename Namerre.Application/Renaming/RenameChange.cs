using System.Text.Json.Serialization;

namespace Namerre.Application.Renaming;

/// <summary>
/// Signals that a durable record changed. <see cref="Kind"/> is <c>Plan</c> or <c>Run</c>; a deleted run
/// reports <c>Deleted</c>. Clients re-read the record instead of trusting this payload.
/// </summary>
/// <param name="OwnerId">The user whose history changed (see <c>PlanView.CreatedBy</c>); only that user and
/// administrators are told. Null for watch folders and history from before users. Never sent to clients.</param>
public sealed record RenameChange(string Kind, string Id, string Status,
    [property: JsonIgnore] long? OwnerId = null);
