using System.Text;

namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>One basic-authentication user as a caller states it: a login name and the bcrypt hash of its password.</summary>
/// <remarks>The record prints without the hash, so a log line formatting it carries no credential material.</remarks>
/// <param name="Name">The login name.</param>
/// <param name="PasswordHash">The bcrypt hash of the password, never the password.</param>
public sealed record SiteBasicAuthUserValue(string Name, string PasswordHash)
{
    /// <summary>Prints the members of the record, leaving the hash out.</summary>
    /// <param name="builder">The builder the record's generated <c>ToString</c> appends to.</param>
    /// <returns>True, as the generated method expects when members were printed.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Name = ").Append(Name).Append(", PasswordHash = [redacted]");
        return true;
    }
}
