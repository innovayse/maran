using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Maran.Host.RateLimiting;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Maran.Host.Authorization;

/// <summary>
/// Authenticates a request that presents a single-use export grant in the <c>Authorization: Bearer</c> header, for
/// the <c>MigrationGrant</c> scheme only (issue #127). The scheme is never the default: an endpoint reaches it by
/// naming a <see cref="MigrationGrantPolicies"/> policy, so no other endpoint ever runs this handler.
/// </summary>
/// <remarks>
/// <para>
/// Written beside <see cref="ApiKeyAuthenticationHandler"/> on purpose, so a reviewer reads it by analogy against
/// code that already passed review. The three things that analogy covers: <b>one header, read here</b> (the grant
/// is taken from <c>Authorization</c> and from nothing else, never the query string or a cookie, so it cannot end
/// up in a URL or a log); <b>one answer for every failure</b> (the same fixed text, the same bodyless 401, whatever
/// the cause); and <b>failures counted before they are expensive</b> (a locked-out address is refused before any
/// lookup or hash, every denial is reported to <see cref="MigrationGrantFailureLimiter"/>, a live grant is never
/// counted).
/// </para>
/// <para>
/// <b>What differs.</b> The principal this builds carries a grant id and the export the grant opens, and NO user,
/// role, account or reseller claim, so no role policy can ever admit it and no tenant filter shows it a row. Its
/// authentication type is the scheme name, which is how <c>HttpContextCurrentUser</c> tells it from a login.
/// </para>
/// </remarks>
public sealed class MigrationGrantAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The text of every failed result: fixed, carrying no detail, and written to no response.</summary>
    private const string FailureText = "MigrationGrant authentication failed.";

    /// <summary>The authenticator that decides whether a presented grant is valid.</summary>
    private readonly IMigrationGrantAuthenticator _authenticator;

    /// <summary>Counts failures per client address and refuses a locked-out address.</summary>
    private readonly MigrationGrantFailureLimiter _failures;

    /// <summary>The clock expiry is judged against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler for one request.</summary>
    /// <param name="options">The scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder the base class requires.</param>
    /// <param name="authenticator">Decides whether a presented grant is valid.</param>
    /// <param name="failures">Counts failures per client address.</param>
    /// <param name="clock">The clock expiry is judged against.</param>
    public MigrationGrantAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IMigrationGrantAuthenticator authenticator,
        MigrationGrantFailureLimiter failures,
        IClock clock)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
        _failures = failures;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var address = ClientAddress.Of(Context.Connection.RemoteIpAddress);
        var now = _clock.UtcNow;

        if (_failures.IsLockedOut(address, now))
        {
            return AuthenticateResult.Fail(FailureText);
        }

        // A header sent twice, or one that is not a bearer value, is not a grant: refused without a lookup,
        // and counted like any failure.
        if (values.Count != 1
            || values[0] is not { } presented
            || !presented.StartsWith(MigrationGrantDefaults.BearerPrefix, StringComparison.Ordinal))
        {
            _failures.RecordFailure(address, now);
            return AuthenticateResult.Fail(FailureText);
        }

        var token = presented[MigrationGrantDefaults.BearerPrefix.Length..];
        var answer = await _authenticator.AuthenticateAsync(token, address, now, Context.RequestAborted);

        switch (answer)
        {
            case MigrationGrantAuthenticated authenticated:
                return AuthenticateResult.Success(BuildTicket(authenticated));
            case MigrationGrantDenied:
                _failures.RecordFailure(address, now);
                return AuthenticateResult.Fail(FailureText);
            default:
                throw new UnreachableException("MigrationGrantAuthentication is sealed to the Sdk: two cases exist.");
        }
    }

    /// <summary>Writes the one 401 every refusal gets: no body, no detail, the same challenge header.</summary>
    /// <param name="properties">The challenge properties; unused, because nothing is disclosed.</param>
    /// <returns>A completed task.</returns>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = MigrationGrantDefaults.Scheme;
        return Task.CompletedTask;
    }

    /// <summary>Builds the ticket for a live grant: its id and its export, and no claim of any other kind.</summary>
    /// <param name="authenticated">The authenticator's answer for the live grant.</param>
    /// <returns>The authentication ticket for the request.</returns>
    private AuthenticationTicket BuildTicket(MigrationGrantAuthenticated authenticated)
    {
        var claims = new List<Claim>
        {
            new(PanelClaimTypes.MigrationGrantId, authenticated.GrantId.ToString()),
            new(PanelClaimTypes.MigrationExportId, authenticated.ExportId.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));

        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
