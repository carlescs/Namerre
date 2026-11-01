using System.Text.Json.Serialization;

namespace Namerre.Application.Authentication;

/// <summary>What a user may do in Namerre. Roles are set by administrators only, never by an identity provider.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    /// <summary>Renames files, manages presets and sees every run.</summary>
    User,
    /// <summary>Everything a user can do, plus user management, watch folders and deleting runs.</summary>
    Admin
}
