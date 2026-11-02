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
/// (<see cref="EnforcedQuotaBackingDto"/>), the agent said it does not
/// (<see cref="NotEnforcedQuotaBackingDto"/>), and the panel could not find out
/// (<see cref="UnmeasuredQuotaBackingDto"/>). The last must not be folded into the second: a
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
[JsonDerivedType(typeof(EnforcedQuotaBackingDto), "enforced")]
[JsonDerivedType(typeof(NotEnforcedQuotaBackingDto), "notEnforced")]
[JsonDerivedType(typeof(UnmeasuredQuotaBackingDto), "unmeasured")]
public abstract record QuotaBackingDto;
