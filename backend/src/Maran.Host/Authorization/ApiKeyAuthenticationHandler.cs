using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Maran.Host.RateLimiting;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Maran.Host.Authorization;

/// <summary>
/// Authenticates a request that presents an API key in the <c>X-Api-Key</c> header, for the
/// <c>ApiKey</c> scheme only. The scheme is never the default: an endpoint reaches it by naming an
/// <see cref="ApiKeyPolicies"/> policy, so no other endpoint ever runs this handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>One header, read here and nowhere else.</b> The key is taken from <c>X-Api-Key</c> and from
/// nothing else — not <c>Authorization</c>, not the query string, not a cookie — so it can neither be
/// mistaken for a bearer token nor end up in a URL, and a cookie can never authenticate a key call.
/// No header is no result (the challenge then answers 401); a header that is present is a presented
/// key.
/// </para>
/// <para>
/// <b>One answer for every failure.</b> A key that is refused for any reason — malformed, unknown,
/// wrong, revoked, expired, outside its address list, or an address that is locked out — produces the
/// same result, <see cref="AuthenticateResult.Fail(string)"/> with a fixed text no one reads, and the
/// challenge writes the same bodyless 401 with the same header whatever the cause. The handler
/// reports nothing about which it was; the reason is for the operator and is logged, if at all, by
/// the authenticator, which never logs the presented value. The work that decides validity, including
/// the digest of an unknown prefix, is the authenticator's and is equal for every failure.
/// </para>
/// <para>
/// <b>Failures are counted before they are expensive.</b> Authorization runs before the rate limiter,
/// so the <c>api</c> limiter never sees a refused key. <see cref="ApiKeyFailureLimiter"/> is asked
/// first, by client address, and a locked-out address is refused before any lookup or hash; every
/// denial is then reported to it. A valid key is never counted.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The text of every failed result. It is fixed and carries no detail; nothing writes it to the
    /// response, and it exists only because a failed result requires some text.
    /// </summary>
    private const string FailureText = "ApiKey authentication failed.";

    /// <summary>The authenticator that decides whether a presented key is valid.</summary>
    private readonly IApiKeyAuthenticator _authenticator;

    /// <summary>Counts failures per client address and refuses a locked-out address.</summary>
    private readonly ApiKeyFailureLimiter _failures;

    /// <summary>The clock expiry is judged against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler for one request.</summary>
    /// <param name="options">The scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder the base class requires.</param>
    /// <param name="authenticator">Decides whether a presented key is valid.</param>
    /// <param name="failures">Counts failures per client address.</param>
    /// <param name="clock">The clock expiry is judged against.</param>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyAuthenticator authenticator,
        ApiKeyFailureLimiter failures,
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
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var address = ClientAddress.Of(Context.Connection.RemoteIpAddress);
        var now = _clock.UtcNow;

        if (_failures.IsLockedOut(address, now))
        {
            return AuthenticateResult.Fail(FailureText);
        }

        // A header sent twice is not a key: refused without a lookup, and counted like any failure.
        if (values.Count != 1 || values[0] is not { } presented)
        {
            _failures.RecordFailure(address, now);
            return AuthenticateResult.Fail(FailureText);
        }

        var answer = await _authenticator.AuthenticateAsync(presented, address, now, Context.RequestAborted);

        switch (answer)
        {
            case ApiKeyAuthenticated authenticated:
                return AuthenticateResult.Success(BuildTicket(authenticated));
            case ApiKeyDenied:
                _failures.RecordFailure(address, now);
                return AuthenticateResult.Fail(FailureText);
            default:
                throw new UnreachableException("ApiKeyAuthentication is sealed to the Sdk: two cases exist.");
        }
    }

    /// <summary>
    /// Writes the one 401 every refusal gets: no body, no detail, and the same challenge header
    /// whether the key was missing, malformed or wrong.
    /// </summary>
    /// <param name="properties">The challenge properties; unused, because nothing is disclosed.</param>
    /// <returns>A completed task.</returns>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyDefaults.Scheme;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the ticket for a valid key: the key id claim, and one scope claim per granted scope the
    /// vocabulary still knows, plus the implicit <see cref="ApiKeyScopes.SelfRead"/>. The principal
    /// carries no subject, role, account or reseller claim, so no role policy can ever admit it.
    /// </summary>
    /// <param name="authenticated">The authenticator's answer for the valid key.</param>
    /// <returns>The authentication ticket for the request.</returns>
    private AuthenticationTicket BuildTicket(ApiKeyAuthenticated authenticated)
    {
        var claims = new List<Claim> { new(PanelClaimTypes.ApiKeyId, authenticated.KeyId.ToString()) };

        // Intersected with the closed vocabulary: a scope this build does not know grants nothing,
        // whatever the stored row says. Self-read is implicit for every valid key.
        var scopes = authenticated.Scopes
            .Append(ApiKeyScopes.SelfRead)
            .Where(scope =>
            {
                return ApiKeyScopes.All.Contains(scope, StringComparer.Ordinal);
            })
            .Distinct(StringComparer.Ordinal);

        claims.AddRange(scopes.Select(scope =>
        {
            return new Claim(PanelClaimTypes.ApiKeyScope, scope);
        }));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
