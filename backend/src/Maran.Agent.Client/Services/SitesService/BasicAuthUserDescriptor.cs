using System.Text;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>One basic-authentication user of a site, as the agent is told to write it.</summary>
/// <remarks>
/// Carries a bcrypt HASH computed by the panel; the agent never sees a password. The hash is still
/// credential material (it allows an offline guess), so the record prints without it: a record's
/// generated <c>ToString</c> would otherwise put every member into whatever log line formats this value.
/// </remarks>
/// <param name="Name">The login name.</param>
/// <param name="PasswordHash">The bcrypt hash of the user's password.</param>
public sealed record BasicAuthUserDescriptor(string Name, string PasswordHash)
{
    /// <summary>Converts this user into its wire message.</summary>
    /// <returns>The populated <see cref="BasicAuthUser"/>.</returns>
    internal BasicAuthUser ToWire()
    {
        return new BasicAuthUser { Name = Name, PasswordHash = PasswordHash };
    }

    /// <summary>Prints the members of the record, leaving the hash out.</summary>
    /// <param name="builder">The builder the record's generated <c>ToString</c> appends to.</param>
    /// <returns>True, as the generated method expects when members were printed.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Name = ").Append(Name).Append(", PasswordHash = [redacted]");
        return true;
    }
}
