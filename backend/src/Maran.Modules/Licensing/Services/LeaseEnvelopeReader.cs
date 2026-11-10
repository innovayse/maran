using System.Globalization;
using System.Text;
using System.Text.Json;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads one lease envelope and judges it: the format, the Ed25519 signature against the lease key pinned in
/// the build, and the payload's fields. The ONE place the lease format is parsed, used by
/// <see cref="FileLeaseSource"/> for the stored file and by <see cref="LeaseRenewer"/> for the licence service's
/// answer BEFORE anything is stored, so a lease is judged the same way going in as coming out.
/// </summary>
/// <remarks>
/// <para>
/// <b>The envelope</b> is the licence's: <c>{"payload": {...}, "signature": "&lt;base64&gt;"}</c>, with the
/// signature over the payload's exact raw text. The payload is
/// <c>{"kind":"lease","licence":"&lt;id&gt;","issued":"&lt;ISO&gt;","validUntil":"&lt;ISO&gt;","server":"&lt;machine id, optional&gt;"}</c>.
/// <c>kind</c> must be <c>lease</c>, so a signed licence payload can never be replayed as a lease even if the
/// two keys were one.
/// </para>
/// <para>
/// <b>Never throws.</b> Anything mistyped, with an end that does not follow its start, or wrongly signed is
/// <see cref="LeaseReading.Refused"/> with its reason. Whose lease it is and whether its dates hold are NOT
/// decided here but by <see cref="Domain.Policies.LeaseGracePolicy"/>, which needs the licence.
/// </para>
/// </remarks>
public sealed class LeaseEnvelopeReader
{
    /// <summary>The value the payload's <c>kind</c> member must have.</summary>
    private const string ExpectedKind = "lease";

    /// <summary>Checks the lease's signature.</summary>
    private readonly ILeaseSignatureVerifier _signatureVerifier;

    /// <summary>Creates the reader.</summary>
    /// <param name="signatureVerifier">Checks the lease's signature.</param>
    public LeaseEnvelopeReader(ILeaseSignatureVerifier signatureVerifier)
    {
        _signatureVerifier = signatureVerifier;
    }

    /// <summary>Judges one lease envelope's text.</summary>
    /// <param name="text">The envelope's text, from the lease file or from the licence service's answer.</param>
    /// <returns>Absent for blank text, a verified lease with its signed bytes, or the refusal with its reason.</returns>
    public LeaseEnvelopeReading Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LeaseEnvelopeReading.Of(LeaseReading.Absent.Instance);
        }

        // Before any parsing: with no key every signature check would fail for a reason the operator
        // cannot act on as "forged"; say what is true instead.
        if (!_signatureVerifier.HasVerificationKey)
        {
            return LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.NoVerificationKey));
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (!root.TryGetProperty("payload", out var payload)
                || !root.TryGetProperty("signature", out var signatureElement)
                || signatureElement.ValueKind != JsonValueKind.String)
            {
                return LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.Malformed));
            }

            var payloadBytes = Encoding.UTF8.GetBytes(payload.GetRawText());
            var signature = Convert.FromBase64String(signatureElement.GetString()!);

            if (!_signatureVerifier.Verify(payloadBytes, signature))
            {
                return LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid));
            }

            var lease = ReadLease(payload);

            return lease is null
                ? LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.Malformed))
                : new LeaseEnvelopeReading(new LeaseReading.Present(lease), new SignedLeasePayload(payloadBytes, signature));
        }
        catch (Exception)
        {
            // A malformed document, a missing or mistyped field, a non-base64 signature: all one
            // refusal, the same boundary the licence verifier draws around its own parsing.
            return LeaseEnvelopeReading.Of(new LeaseReading.Refused(LeaseRefusalReason.Malformed));
        }
    }

    /// <summary>Reads the payload's fields, or null when any is missing, mistyped or inconsistent.</summary>
    /// <param name="payload">The payload element.</param>
    /// <returns>The lease, or null.</returns>
    private static LicenceLease? ReadLease(JsonElement payload)
    {
        if (!TryString(payload, "kind", out var kind) || !string.Equals(kind, ExpectedKind, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryString(payload, "licence", out var licenceId) || string.IsNullOrWhiteSpace(licenceId))
        {
            return null;
        }

        if (!TryInstant(payload, "issued", out var issuedAt) || !TryInstant(payload, "validUntil", out var validUntil))
        {
            return null;
        }

        // A lease that ends before it starts is not a lease an issuer meant to make.
        if (validUntil <= issuedAt)
        {
            return null;
        }

        // `server` is optional; present but not a string is malformed, as for the licence.
        string? machineId = null;
        if (payload.TryGetProperty("server", out _) && !TryString(payload, "server", out machineId))
        {
            return null;
        }

        return new LicenceLease(licenceId!, issuedAt, validUntil, machineId);
    }

    /// <summary>Reads a required string member.</summary>
    /// <param name="payload">The payload element.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The string, when present.</param>
    /// <returns>True when the member exists and is a string.</returns>
    private static bool TryString(JsonElement payload, string name, out string? value)
    {
        if (payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Reads a required ISO-8601 instant member.</summary>
    /// <param name="payload">The payload element.</param>
    /// <param name="name">The member name.</param>
    /// <param name="instant">The instant, when it parses.</param>
    /// <returns>True when the member exists and parses.</returns>
    private static bool TryInstant(JsonElement payload, string name, out DateTimeOffset instant)
    {
        instant = default;

        return TryString(payload, name, out var text)
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out instant);
    }
}
