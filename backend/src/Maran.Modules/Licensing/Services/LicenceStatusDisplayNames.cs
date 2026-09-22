using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Resolves, in the current request or process culture, the operator-facing sentence for a
/// <see cref="LicenceStatus"/> the panel just observed.
/// </summary>
/// <remarks>
/// <para>
/// Built in the same shape as the Databases module's <c>GrantRepairRefusalDisplayNames</c>: the
/// refusal vocabulary is this assembly's own closed enum
/// (<see cref="LicenceRefusalReason"/>), so the lookup is a plain key concatenation rather than a
/// dictionary that could fall out of sync with the enum. Unlike that type, this one's reason
/// parameter is the enum itself, not a string — <see cref="LicenceRefusalReason"/> is declared in
/// this assembly (nothing external reports it the way the agent reports grant-repair reasons as
/// strings over the wire), so there is no "an unknown future value" case to fall back for; every
/// member this build can construct has an entry, checked by this module's own tests.
/// </para>
/// </remarks>
public sealed class LicenceStatusDisplayNames
{
    /// <summary>Prefix of the resx key holding what the verifier decided.</summary>
    private const string RefusalKeyPrefix = "LicenceRefusal";

    /// <summary>Prefix of the resx key holding what the operator can do about it.</summary>
    private const string AdviceKeyPrefix = "LicenceAdvice";

    /// <summary>Prefix of the resx key holding the wire <c>State</c> field's short display label.</summary>
    private const string StateKeyPrefix = "LicenceState";

    /// <summary>This module's display-name resources for the current culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public LicenceStatusDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>States the sentence an operator reads for one <see cref="LicenceStatus"/>.</summary>
    /// <param name="status">The status the panel just observed.</param>
    /// <returns>
    /// The localized status sentence: <c>LicenceStatusValid</c>, <c>LicenceStatusAbsent</c>, or the
    /// refusal name for <see cref="LicenceStatus.Refused"/> (see <see cref="AdviceFor"/> for the
    /// matching next step).
    /// </returns>
    public string SentenceFor(LicenceStatus status)
    {
        return status switch
        {
            LicenceStatus.Valid => _displayNames["LicenceStatusValid"],
            LicenceStatus.Absent => _displayNames["LicenceStatusAbsent"],
            LicenceStatus.Refused refused => _displayNames[RefusalKeyPrefix + refused.Reason],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown LicenceStatus case."),
        };
    }

    /// <summary>
    /// States the sentence an operator reads on a SUCCESSFUL install response, distinct from
    /// <see cref="SentenceFor"/>'s plain "installed and verified": this one states outright that
    /// whether the licence is tied to THIS server is decided by the licence's own `server` claim, so
    /// the response text itself carries the honesty obligation rather than leaving it implied. It
    /// states the conditional rather than a verdict because this sentence is one string for both
    /// kinds of licence, and a sentence that promised binding to a holder of an unbound licence
    /// would be the same defect in the other direction.
    /// </summary>
    /// <returns>The localized install-success sentence.</returns>
    public string InstalledSentence()
    {
        return _displayNames["LicenceInstalledSentence"];
    }

    /// <summary>States what the operator can do about a refusal.</summary>
    /// <param name="reason">The specific reason the licence was refused.</param>
    /// <returns>The localized advice sentence.</returns>
    public string AdviceFor(LicenceRefusalReason reason)
    {
        return _displayNames[AdviceKeyPrefix + reason];
    }

    /// <summary>
    /// Names, on the wire, the short label an operator reads for <see cref="Common.LicenceStatusDto.State"/>.
    /// </summary>
    /// <param name="state">
    /// The machine value already placed on <see cref="Common.LicenceStatusDto.State"/> — <c>"Valid"</c>,
    /// <c>"Absent"</c> or <c>"Refused"</c>.
    /// </param>
    /// <returns>
    /// The localized label, or <paramref name="state"/> itself when this build carries no entry for it.
    /// </returns>
    /// <remarks>
    /// <see cref="Common.LicenceStatusDto"/> carries <c>State</c> as a <see cref="string"/>, not the
    /// closed <see cref="LicenceStatus"/> hierarchy itself — the DTO is the panel's outward wire shape,
    /// and <c>rules/architecture.md</c>'s display-name law treats every outward string identifier as an
    /// open set (a build that has not shipped a name for a value it does not recognize must still show
    /// something, not an empty label or a raw resource key), the same fallback
    /// <c>Services.BackupFailureDisplayNames.Of</c> uses for the Backups module's own open string set.
    /// In practice <see cref="LicenceStatus"/> is closed and every case is covered by this module's own
    /// resx triple and tests; the fallback exists for the wire shape's own contract, not because a
    /// fourth state is expected.
    /// </remarks>
    public string StateNameFor(string state)
    {
        var localized = _displayNames[StateKeyPrefix + state];

        return localized.ResourceNotFound ? state : localized.Value;
    }

    /// <summary>
    /// Names, on the wire, the short label an operator reads for
    /// <see cref="Common.LicenceStatusDto.RefusalReason"/>.
    /// </summary>
    /// <param name="reasonCode">
    /// The machine value already placed on <see cref="Common.LicenceStatusDto.RefusalReason"/> — one of
    /// <see cref="LicenceRefusalReason"/>'s members, stringified.
    /// </param>
    /// <returns>
    /// The localized label, or <paramref name="reasonCode"/> itself when this build carries no entry
    /// for it.
    /// </returns>
    /// <remarks>
    /// Reuses the same <c>LicenceRefusal&lt;Reason&gt;</c> entries <see cref="SentenceFor"/> already
    /// resolves for a <see cref="LicenceStatus.Refused"/> case — one name, read from two call sites, the
    /// wire's own <c>string</c> field and the strongly-typed enum. See <see cref="StateNameFor"/>'s
    /// remarks for why the fallback exists despite the reason set being closed today.
    /// </remarks>
    public string ReasonNameFor(string reasonCode)
    {
        var localized = _displayNames[RefusalKeyPrefix + reasonCode];

        return localized.ResourceNotFound ? reasonCode : localized.Value;
    }
}
