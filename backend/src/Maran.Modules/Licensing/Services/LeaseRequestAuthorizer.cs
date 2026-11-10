using System.Net.Http.Headers;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Interfaces;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Attaches the installed licence's lease to an outgoing request as <c>Authorization: Bearer</c>, so the
/// licence service can see which licence is asking and state what it entitles. The marketplace asks for it
/// through the Sdk's <see cref="ILeaseRequestAuthorizer"/> and never sees the credential.
/// </summary>
/// <remarks>
/// <para>
/// <b>It attaches only a lease that is good now.</b> The licence must verify, and its lease must stand
/// Fresh or RenewalOverdue (a late renewal keeps the cover, so it keeps the credential). A missing,
/// refused, ended, future-dated or foreign lease, an offline licence (which holds no lease), a licence that
/// does not verify and a build with no lease key all answer false and leave the request untouched, so the
/// caller reports <c>LeaseRequired</c> instead of sending a credential the service would refuse. The same
/// judgement that gates a paid module (<see cref="LicenceCoverageCache"/>) decides, so the two cannot
/// disagree about whether the panel has a lease.
/// </para>
/// <para>
/// <b>The credential is the lease as issued</b> (<see cref="LeaseBearerToken"/>): the service's own signed
/// statement, handed back unchanged. Nothing of the panel's, the host's or a customer's is added, and the
/// token is never logged, journalled or kept: it is built from the file for the request and dropped.
/// </para>
/// <para>
/// <b>Stated limit.</b> A bearer lease is replayable by whoever sees it until it ends, at most its 14-day
/// life; TLS is what protects it in transit, and the service binds a grant to the lease's <c>server</c>
/// member, which only a lease issued for one machine carries
/// (docs/superpowers/notes/2026-11-07-marketplace-loader-threat-note.md, R9).
/// </para>
/// </remarks>
public sealed class LeaseRequestAuthorizer : ILeaseRequestAuthorizer
{
    /// <summary>The authentication scheme of the credential.</summary>
    private const string Scheme = "Bearer";

    /// <summary>Judges whether the panel has a lease that is good now.</summary>
    private readonly LicenceCoverageCache _coverage;

    /// <summary>Reads the signed bytes of the stored lease.</summary>
    private readonly ILeaseEnvelopeSource _envelope;

    /// <summary>Creates the authorizer.</summary>
    /// <param name="coverage">Judges whether the panel has a lease that is good now.</param>
    /// <param name="envelope">Reads the signed bytes of the stored lease.</param>
    public LeaseRequestAuthorizer(LicenceCoverageCache coverage, ILeaseEnvelopeSource envelope)
    {
        _coverage = coverage;
        _envelope = envelope;
    }

    /// <inheritdoc />
    public async Task<bool> TryAuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var coverage = await _coverage.GetAsync(ct);
        if (coverage.Status is not LicenceStatus.Valid
            || coverage.Lease.Kind is not (LeaseStandingKind.Fresh or LeaseStandingKind.RenewalOverdue))
        {
            return false;
        }

        var signed = await _envelope.ReadSignedAsync(ct);
        if (signed is null)
        {
            return false;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, LeaseBearerToken.Of(signed));
        return true;
    }
}
