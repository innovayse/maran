namespace Maran.Modules.Licensing.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every operator-facing sentence
/// this module owns: <c>LicensingModuleDisplayName</c> (resolved via <see cref="LicensingManifest"/>'s
/// <c>DisplayNameKey</c>), <c>LicenceStatusAbsent</c> and <c>LicenceStatusValid</c> for the two
/// non-refusal outcomes of <see cref="Domain.Enums.LicenceStatus"/>, one <c>LicenceState&lt;State&gt;</c>
/// short label per case of that same type (the wire's <c>Common.LicenceStatusDto.StateDisplayName</c>),
/// and one <c>LicenceRefusal&lt;Reason&gt;</c> plus one <c>LicenceAdvice&lt;Reason&gt;</c> entry per
/// member of <see cref="Domain.Enums.LicenceRefusalReason"/> (the former doing double duty as
/// <c>Common.LicenceStatusDto.RefusalReasonDisplayName</c>).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>Resources/ErrorMessages.resx</c>: that file's keys are machine-stable error
/// CODES, discovered by <c>ErrorCodeCensus</c> and required to carry a decided
/// <c>Maran.SharedKernel.Results.ErrorType</c> in every test run (backend/tests/Maran.Sdk.Tests). A
/// refused licence is not, today, translated into an <c>Error</c> anywhere — this slice adds no
/// controller, no query, and no <c>Result</c>-returning handler (see <see cref="LicensingModule"/>'s
/// own remarks) — so there is no HTTP status this text answers yet, and forcing one now would be a
/// decision this slice was not asked to make. When a future slice adds the query that reads
/// <see cref="Domain.Enums.LicenceStatus"/> and returns it as a <c>Result</c>, that slice is where
/// these entries (or new ones shaped like them) move into <c>ErrorMessages.resx</c> and gain a kind
/// in <c>ExpectedErrorStatuses</c> — not before there is a caller to classify them for.
/// </para>
/// <para>
/// The refusal entries come in PAIRS, the same shape the Databases module's grant-repair refusals
/// use: naming a refusal says what the verifier decided, and the advice says what the operator can
/// do about it. <c>LicenceStatusAbsent</c> carries no advice pair on purpose — "install a licence"
/// is already the whole of its own sentence, and giving the ordinary first-run state a second entry
/// would make it look like the four genuine refusals, which is exactly the collapse
/// <see cref="Domain.Enums.LicenceStatus"/>'s own remarks were written to prevent.
/// </para>
/// </remarks>
public sealed class DisplayNames
{
}
