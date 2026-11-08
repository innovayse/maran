using System.Text;

namespace Maran.Sdk.Contracts;

/// <summary>One basic-authentication user of a site, as one module reports it to another.</summary>
/// <remarks>
/// Carries the bcrypt HASH, because installing a certificate rewrites the vhost and the agent must be
/// told the credentials again. The hash is credential material, so the record prints without it.
/// </remarks>
/// <param name="Name">The login name.</param>
/// <param name="PasswordHash">The bcrypt hash of the user's password.</param>
public sealed record SiteBasicAuthUserSnapshot(string Name, string PasswordHash)
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
