using System.Globalization;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Models;
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

    /// <summary>Prefix of the resource keys naming a lease's standing, completed by the standing's name.</summary>
    private const string LeaseKeyPrefix = "LicenceLease";

    /// <summary>Prefix of the resource keys naming what a lease request amounted to, completed by the outcome's name.</summary>
    private const string LeaseRequestKeyPrefix = "LicenceLeaseRequest";

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
    /// <param name="state">The case already placed on <see cref="Common.LicenceStatusDto.State"/>.</param>
    /// <returns>The localized label.</returns>
    /// <remarks>
    /// <see cref="LicenceStateKind"/> is closed, so there is no fallback for an unrecognised value:
    /// every member has an entry in this module's resx triple, checked by this module's own tests, as
    /// <see cref="AdviceFor"/> relies on for <see cref="LicenceRefusalReason"/>.
    /// </remarks>
    public string StateNameFor(LicenceStateKind state)
    {
        return _displayNames[StateKeyPrefix + state];
    }

    /// <summary>
    /// Names, on the wire, the short label an operator reads for
    /// <see cref="Common.LicenceStatusDto.RefusalReason"/>.
    /// </summary>
    /// <param name="reason">The reason already placed on <see cref="Common.LicenceStatusDto.RefusalReason"/>.</param>
    /// <returns>The localized label.</returns>
    /// <remarks>
    /// Reuses the same <c>LicenceRefusal&lt;Reason&gt;</c> entries <see cref="SentenceFor"/> already
    /// resolves for a <see cref="LicenceStatus.Refused"/> case — one name, read from two call sites.
    /// See <see cref="StateNameFor"/>'s remarks for why there is no fallback.
    /// </remarks>
    public string ReasonNameFor(LicenceRefusalReason reason)
    {
        return _displayNames[RefusalKeyPrefix + reason];
    }

    /// <summary>The short, localized label of a lease's standing.</summary>
    /// <param name="standing">The standing.</param>
    /// <returns>The label in the request's culture.</returns>
    public string LeaseNameFor(LeaseStandingKind standing)
    {
        return _displayNames[LeaseKeyPrefix + standing];
    }

    /// <summary>The sentence an administrator reads for what a request for a lease amounted to.</summary>
    /// <param name="outcome">What the renewal pass the install asked for did.</param>
    /// <returns>The sentence in the request's culture, saying what happened and whether the panel will try again.</returns>
    public string LeaseRequestNameFor(LeaseRenewalOutcome outcome)
    {
        return _displayNames[LeaseRequestKeyPrefix + outcome];
    }

    /// <summary>The label that precedes the server id on a console line.</summary>
    /// <returns>The label in the process culture.</returns>
    public string ServerIdLabel()
    {
        return _displayNames["LicenceServerIdLabel"];
    }

    /// <summary>The word that stands where the server id would be when it cannot be read.</summary>
    /// <returns>The word in the process culture.</returns>
    public string ServerIdUnavailable()
    {
        return _displayNames["LicenceServerIdUnavailable"];
    }

    /// <summary>The sentence that tells an administrator the licence is close to its expiry.</summary>
    /// <param name="warning">The warning, from <see cref="LicenceExpiryWarningSource"/>.</param>
    /// <returns>The sentence in the current culture, naming the expiry day (UTC, <c>yyyy-MM-dd</c>) and the days left.</returns>
    public string ExpiryWarningFor(LicenceExpiryWarning warning)
    {
        return _displayNames[
            "LicenceExpiryWarning",
            warning.Expiry.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            warning.DaysLeft.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>The steps that renew a licence: claim it again on the marketplace, or install a new licence file.</summary>
    /// <returns>The steps in the current culture. They never carry the claim link, which the caller adds beside them.</returns>
    public string ExpiryRenewalAdvice()
    {
        return _displayNames["LicenceExpiryRenewalAdvice"];
    }
}
