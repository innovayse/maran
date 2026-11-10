using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The verdict <see cref="Policies.LeaseGracePolicy"/> reaches about a lease: its
/// <see cref="LeaseStandingKind"/> and, when a trusted lease was read, when it ends. It carries no
/// lease text, signature or machine id, so it is safe to put in a status answer and to cache.
/// </summary>
/// <param name="Kind">Where the lease stands.</param>
/// <param name="ValidUntil">
/// When the lease stops (or stopped) being valid; set for <see cref="LeaseStandingKind.Fresh"/>,
/// <see cref="LeaseStandingKind.RenewalOverdue"/> and <see cref="LeaseStandingKind.Expired"/>, null for
/// every other kind.
/// </param>
public sealed record LeaseStanding(LeaseStandingKind Kind, DateTimeOffset? ValidUntil)
{
    /// <summary>The standing of a licence that has no lease because none is stored.</summary>
    public static LeaseStanding Missing { get; } = new(LeaseStandingKind.Missing, null);

    /// <summary>The standing of a licence that asks for no lease (offline).</summary>
    public static LeaseStanding NotRequired { get; } = new(LeaseStandingKind.NotRequired, null);
}
