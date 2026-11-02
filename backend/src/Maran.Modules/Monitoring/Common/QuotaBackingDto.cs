using System.Text.Json.Serialization;

namespace Maran.Modules.Monitoring.Common;

/// <summary>
/// Whether the kernel backs an account's disk allowance, as exactly one of three cases, each its own
/// type, so the explanation exists only on the cases that need one.
/// </summary>
/// <remarks>
/// <para>
/// This is issue #29's own fix: the panel showed the same bare number whether or not anything
/// backed it, which made an unenforced paid limit undetectable by construction until the disk
/// filled. The three cases are three different facts: the agent said the filesystem enforces quotas
/// (<see cref="Enforced"/>), the agent said it does not (<see cref="NotEnforced"/>), and the panel
/// could not find out (<see cref="Unmeasured"/>). The last must not be folded into the second: a
/// sentence asserting the filesystem is not enforcing, shown for an agent that merely failed to
/// answer, states a fact nobody measured.
/// </para>
/// <para>
/// On the wire the case travels as a <c>state</c> member (<c>enforced</c>, <c>notEnforced</c>,
/// <c>unmeasured</c>) beside the fields that case carries. The sentences are rendered by the
/// backend in the caller's language (rules/architecture.md, "the backend owns the data"), so the
/// SPA never invents wording for why a real, paid-for allowance is not being applied.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(QuotaBackingDto.Enforced), "enforced")]
[JsonDerivedType(typeof(QuotaBackingDto.NotEnforced), "notEnforced")]
[JsonDerivedType(typeof(QuotaBackingDto.Unmeasured), "unmeasured")]
public abstract record QuotaBackingDto
{
    /// <summary>Closes the hierarchy to the three cases declared below.</summary>
    private QuotaBackingDto()
    {
    }

    /// <summary>
    /// The filesystem holding this account's home is tracking quota accounting right now, so the
    /// allowance is backed by a kernel-enforced limit and there is nothing to explain.
    /// </summary>
    public sealed record Enforced : QuotaBackingDto;

    /// <summary>
    /// The agent reported that the filesystem cannot enforce a quota right now. The allowance is
    /// real — it is still the plan the customer paid for — but nothing on the server refuses a write
    /// past it.
    /// </summary>
    /// <param name="Note">The localized explanation of what is wrong and what restores enforcement.</param>
    public sealed record NotEnforced(string Note) : QuotaBackingDto;

    /// <summary>
    /// The panel could not find out whether the filesystem enforces a quota: the agent's answer did
    /// not arrive or could not be read. Neither "enforced" nor "not enforced" is claimed.
    /// </summary>
    /// <param name="Note">The localized statement that enforcement could not be determined.</param>
    public sealed record Unmeasured(string Note) : QuotaBackingDto;
}
