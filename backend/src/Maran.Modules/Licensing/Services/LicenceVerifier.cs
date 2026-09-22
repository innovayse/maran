using System.Text;
using System.Text.Json;
using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Verifies a licence artefact offline: parse, signature, product, expiry — and answers the
/// three-state <see cref="LicenceStatus"/> (spec §228). This is the module's own orchestrator, not
/// the Sdk seam a closed <c>PluginLoader</c> would call (that thin façade is a later slice's work);
/// this type is what the façade would wrap.
/// </summary>
/// <remarks>
/// <para>
/// <b>How §229 ("ядро не умирает никогда" — the core never dies) is held here, structurally rather
/// than by convention.</b> <see cref="VerifyAsync"/> has a non-nullable, non-faulting return type:
/// <see cref="Task{LicenceStatus}"/>, where <see cref="LicenceStatus"/> itself has no "exception"
/// case. Every internal step — JSON parse, base64 decode, missing/mistyped field, signature check,
/// product check, expiry check — is performed inside ONE try/catch that maps every failure to
/// <see cref="LicenceRefusalReason.Malformed"/>, and nothing downstream of this method can observe
/// anything other than a <see cref="LicenceStatus"/> value. This is deliberately not "a try/catch
/// someone could delete" — it is the ONLY code path out of this method, so there is no second path
/// for a future edit to forget to guard.
/// </para>
/// <para>
/// The broad <c>catch (Exception)</c> below is therefore intentional, not an oversight this file's
/// author meant to narrow later: <c>docs/superpowers/notes/2026-09-22-licence-verification-threat-
/// note.md</c> §2 argues that failing OPEN on a parse or verification bug is the correct direction
/// for this specific mechanism — unlike almost every other refusal in <c>rules/security.md</c>,
/// where "fail closed" is correct — because the alternative (a verification bug propagating into a
/// thrown exception on a startup or Host-composition path) is exactly the shape that would let one
/// verifier defect take a customer's whole panel down, which §229 forbids by name.
/// </para>
/// </remarks>
public sealed class LicenceVerifier
{
    /// <summary>This build's own product identifier, checked against a licence's <c>product</c> field.</summary>
    private const string ExpectedProduct = "maran";

    /// <summary>This module's seam over Ed25519 signature checking.</summary>
    private readonly ILicenceSignatureVerifier _signatureVerifier;

    /// <summary>The panel's injected time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the verifier.</summary>
    /// <param name="signatureVerifier">This module's seam over Ed25519 signature checking.</param>
    /// <param name="clock">The panel's injected time source.</param>
    public LicenceVerifier(ILicenceSignatureVerifier signatureVerifier, IClock clock)
    {
        _signatureVerifier = signatureVerifier;
        _clock = clock;
    }

    /// <summary>Verifies a licence artefact's text and answers a three-state result.</summary>
    /// <param name="rawLicenceText">
    /// The installed licence envelope's raw text — a JSON object with a <c>payload</c> object and a
    /// base64 <c>signature</c> string — or <c>null</c>/empty/whitespace when nothing is installed.
    /// </param>
    /// <param name="cancellationToken">
    /// Accepted for the shape a future I/O-bound implementation (reading the installed licence from
    /// disk or the database, out of scope for this pass) would need; unused by this pure, offline
    /// pass, which performs no I/O and cannot be cancelled mid-computation.
    /// </param>
    /// <returns>
    /// <see cref="LicenceStatus.Valid"/>, <see cref="LicenceStatus.Absent"/>, or
    /// <see cref="LicenceStatus.Refused"/> — never a thrown exception, for any input whatsoever.
    /// </returns>
    public Task<LicenceStatus> VerifyAsync(string? rawLicenceText, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verify(rawLicenceText));
    }

    /// <summary>The synchronous core of <see cref="VerifyAsync"/>; see its remarks for the §229 argument.</summary>
    /// <param name="rawLicenceText">The installed licence envelope's raw text, or absent.</param>
    /// <returns>The three-state result.</returns>
    private LicenceStatus Verify(string? rawLicenceText)
    {
        if (string.IsNullOrWhiteSpace(rawLicenceText))
        {
            // The brief's own named trap: an absent licence is its own case, never
            // Refused(Malformed) and never Refused(anything) — see LicenceStatus's remarks.
            return LicenceStatus.Absent.Instance;
        }

        try
        {
            using var document = JsonDocument.Parse(rawLicenceText);
            var root = document.RootElement;

            if (!root.TryGetProperty("payload", out var payloadElement)
                || !root.TryGetProperty("signature", out var signatureElement)
                || signatureElement.ValueKind != JsonValueKind.String)
            {
                return Refused(LicenceRefusalReason.Malformed);
            }

            var payloadJson = payloadElement.GetRawText();
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var signatureBytes = Convert.FromBase64String(signatureElement.GetString()!);

            if (!_signatureVerifier.Verify(payloadBytes, signatureBytes))
            {
                return Refused(LicenceRefusalReason.SignatureInvalid);
            }

            var licence = ParseLicenceFields(payloadElement);
            if (licence is null)
            {
                return Refused(LicenceRefusalReason.Malformed);
            }

            if (!string.Equals(licence.Product, ExpectedProduct, StringComparison.Ordinal))
            {
                return Refused(LicenceRefusalReason.ProductMismatch);
            }

            if (LicenceExpiryPolicy.IsExpired(licence, _clock))
            {
                return Refused(LicenceRefusalReason.Expired);
            }

            // Fingerprint comparison is intentionally absent here: unreachable in this slice, see
            // LicenceRefusalReason.FingerprintMismatch's own remarks for why.
            return new LicenceStatus.Valid(licence);
        }
        catch (Exception)
        {
            // Every internal failure this method's own body can produce — a malformed JSON
            // document, a missing or mistyped field, a non-base64 signature, an unparsable date —
            // lands here and is reported as Malformed. See this type's own remarks for why this is
            // the ONE place that boundary is enforced, rather than a convention callers must repeat.
            return Refused(LicenceRefusalReason.Malformed);
        }
    }

    /// <summary>Parses the licence's own fields out of the payload element.</summary>
    /// <param name="payloadElement">The <c>payload</c> object of the licence envelope.</param>
    /// <returns>The parsed licence, or <c>null</c> when a required field is missing or mistyped.</returns>
    private static Licence? ParseLicenceFields(JsonElement payloadElement)
    {
        if (!payloadElement.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!payloadElement.TryGetProperty("product", out var productElement) || productElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!payloadElement.TryGetProperty("tier", out var tierElement) || tierElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!payloadElement.TryGetProperty("modules", out var modulesElement) || modulesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (!payloadElement.TryGetProperty("expiry", out var expiryElement) || expiryElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var modules = new List<string>();
        foreach (var moduleElement in modulesElement.EnumerateArray())
        {
            if (moduleElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            modules.Add(moduleElement.GetString()!);
        }

        if (!DateTimeOffset.TryParse(
                expiryElement.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var expiry))
        {
            return null;
        }

        return new Licence(LicenceId.Of(id), productElement.GetString()!, tierElement.GetString()!, modules, expiry);
    }

    /// <summary>Builds a <see cref="LicenceStatus.Refused"/> for the given reason.</summary>
    /// <param name="reason">The specific, actionable refusal reason.</param>
    /// <returns>The refused status.</returns>
    private static LicenceStatus.Refused Refused(LicenceRefusalReason reason)
    {
        return new LicenceStatus.Refused(reason);
    }
}
