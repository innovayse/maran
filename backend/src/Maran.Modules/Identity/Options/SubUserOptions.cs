using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// How many people one account's owner may add. Bound from the <c>SubUsers</c> configuration
/// section and validated at startup (rules/csharp.md "Options validated at startup").
/// </summary>
/// <remarks>
/// A cap is the bound on two things at once. Every sub-user is a login that can sign in, so an
/// unbounded team is an unbounded number of credentials on one account; and every invitation is a
/// row the owner can create by typing a name, so an unbounded team is also a free write amplifier
/// and, together with the invitation's uniform answer, a bound on how many names an owner can test.
/// Pending invitations count: an unaccepted login is already a row and already a mailed token.
/// </remarks>
public sealed class SubUserOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "SubUsers";

    /// <summary>
    /// The most sub-users (invited or active) one account may hold. The owner is not counted.
    /// </summary>
    /// <remarks>
    /// Twenty-five covers a large agency team on one hosting account. The range has a floor of one
    /// so a cap of zero — which would silently turn the feature off while the screen still offered
    /// it — stops the boot instead.
    /// </remarks>
    [Range(1, 1_000)]
    public int MaxPerAccount { get; set; } = 25;
}
