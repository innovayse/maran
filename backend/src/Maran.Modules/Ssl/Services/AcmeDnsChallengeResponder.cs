using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Proves control of the order's name and its wildcard over DNS-01: publishes one TXT record per pending
/// authorization, asks the authority to read them, and waits, bounded, for it to accept every one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every record is published before any challenge is answered.</b> A name and its wildcard are proven at
/// the same label, so the authority must find both values there when it looks. Answering the first
/// authorization before the second record exists would let a caching resolver keep the first answer and
/// miss the second.
/// </para>
/// <para>
/// <b>An authorization that is already valid publishes nothing.</b> Renewal is that case: authorities cache
/// authorizations, so the order usually comes back ready or with every authorization valid, and the zone is
/// not touched at all.
/// </para>
/// <para>
/// <b>Every record is removed on every path</b> — success, a refused challenge, a failed call, cancellation —
/// in a <c>finally</c> that does not honour the caller's cancellation, and a removal that fails (or throws)
/// never turns a validation that succeeded into a failure. The record is a single-use proof that is worthless
/// once its authorization is consumed, while discarding a valid authorization over a failed removal would
/// cost a real certificate; the DNS module sweeps a record that outlives its hour.
/// </para>
/// <para>
/// <b>The authority's words stay out.</b> A refused DNS challenge is worded by the problem TYPE it names
/// (<c>dns</c>, <c>caa</c> or <c>unauthorized</c> become <c>AcmeDnsChallengeFailed</c>, whose sentence tells the
/// customer what to check), and only that type is logged, never the prose (rules/security.md item 8).
/// </para>
/// </remarks>
internal sealed class AcmeDnsChallengeResponder : IAcmeChallengeResponder
{
    /// <summary>The authorization status meaning "control of this name is already proven".</summary>
    private const string AuthorizationValid = "valid";

    /// <summary>The prefix every ACME problem type URN shares (RFC 8555 §6.7).</summary>
    private const string ProblemPrefix = "urn:ietf:params:acme:error:";

    /// <summary>The problem types that mean "the name does not resolve to what the order proved", each reported with the same hint.</summary>
    private static readonly string[] DnsProblemTypes = [ProblemPrefix + "dns", ProblemPrefix + "caa", ProblemPrefix + "unauthorized"];

    /// <summary>Pre-compiled log delegate for an authorization the authority judged unsuccessful.</summary>
    private static readonly Action<ILogger, string, Exception?> LogChallengeRefused =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(AcmeDnsChallengeResponder)),
            "The certificate authority refused a DNS challenge with problem type {ProblemType}");

    /// <summary>Pre-compiled log delegate for a challenge record that could not be removed.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRemovalFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(AcmeDnsChallengeResponder)),
            "A DNS challenge record could not be removed ({Reason}); the DNS module sweeps it within the hour");

    /// <summary>Publishes and removes the challenge records.</summary>
    private readonly IAcmeDnsChallengeWriter _writer;

    /// <summary>Waits for the authority's judgement.</summary>
    private readonly AcmeAuthorizationPoller _poller;

    /// <summary>Where the authority's problem type and a failed removal are noted.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates the responder.</summary>
    /// <param name="writer">Publishes and removes the DNS-01 challenge records.</param>
    /// <param name="poller">Waits for the authority's judgement.</param>
    /// <param name="logger">Sink for the authority's problem type and a failed removal.</param>
    public AcmeDnsChallengeResponder(IAcmeDnsChallengeWriter writer, AcmeAuthorizationPoller poller, ILogger logger)
    {
        _writer = writer;
        _poller = poller;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> RespondAsync(
        AcmeSession session,
        IReadOnlyList<string> authorizationUrls,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Challenge is not DnsZoneChallenge target)
        {
            throw new ArgumentException("A DNS-01 responder answers an order whose proof is a DNS record.", nameof(request));
        }

        var published = new List<DnsChallengeHandle>();
        try
        {
            var pending = new List<PendingChallenge>();
            foreach (var authorizationUrl in authorizationUrls)
            {
                var offered = await OfferedAsync(session, authorizationUrl, cancellationToken);
                if (!offered.IsSuccess)
                {
                    return Result.Fail(offered.Error!);
                }

                if (offered.Value is not { } challenge)
                {
                    continue; // already valid: nothing to prove, nothing to publish
                }

                var handle = await _writer.PublishAsync(
                    target.AccountId,
                    request.Domain,
                    TxtValue(challenge.Token, session.Thumbprint),
                    cancellationToken);
                if (!handle.IsSuccess)
                {
                    return Result.Fail(handle.Error!);
                }

                published.Add(handle.Value);
                pending.Add(new PendingChallenge(authorizationUrl, challenge.Url));
            }

            foreach (var challenge in pending)
            {
                var triggered = await session.PostAsync(challenge.ChallengeUrl, "{}", cancellationToken);
                if (!triggered.IsSuccess)
                {
                    return Result.Fail(
                        Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.UpstreamRefused));
                }
            }

            foreach (var challenge in pending)
            {
                var accepted = await _poller.PollAsync(session, challenge.AuthorizationUrl, WordInvalid, cancellationToken);
                if (!accepted.IsSuccess)
                {
                    return accepted;
                }
            }

            return Result.Ok();
        }
        finally
        {
            foreach (var handle in published)
            {
                await RemoveQuietlyAsync(handle);
            }
        }
    }

    /// <summary>Builds the TXT value that answers a DNS-01 challenge: the base64url SHA-256 of the key authorization (RFC 8555 §8.4).</summary>
    /// <param name="token">The challenge token from the authority.</param>
    /// <param name="thumbprint">The account key's RFC 7638 thumbprint.</param>
    /// <returns>Forty-three base64url characters, which is the only shape the DNS module accepts.</returns>
    private static string TxtValue(string token, string thumbprint)
    {
        return AcmeSigner.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes($"{token}.{thumbprint}")));
    }

    /// <summary>Finds the DNS-01 challenge among the ones the authority offered.</summary>
    /// <param name="authorization">The authorization object.</param>
    /// <returns>The challenge, or <c>null</c> when none carries both its URL and its token.</returns>
    private static OfferedChallenge? ReadDnsChallenge(JsonElement authorization)
    {
        if (!authorization.TryGetProperty("challenges", out var challenges)
            || challenges.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var challenge in challenges.EnumerateArray())
        {
            if (!string.Equals(AcmeJson.Text(challenge, "type"), "dns-01", StringComparison.Ordinal))
            {
                continue;
            }

            var url = AcmeJson.Text(challenge, "url");
            var token = AcmeJson.Text(challenge, "token");
            return url.Length > 0 && token.Length > 0 ? new OfferedChallenge(url, token) : null;
        }

        return null;
    }

    /// <summary>Reads the problem type an unsuccessful challenge names.</summary>
    /// <param name="authorization">The authorization document the authority judged unsuccessful.</param>
    /// <returns>The problem type URN of the first challenge that carries an error, or the empty string.</returns>
    private static string ProblemTypeOf(JsonElement authorization)
    {
        if (!authorization.TryGetProperty("challenges", out var challenges)
            || challenges.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var challenge in challenges.EnumerateArray())
        {
            if (challenge.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return AcmeJson.Text(error, "type");
            }
        }

        return string.Empty;
    }

    /// <summary>Reads one authorization and, when it still needs proof, the DNS-01 challenge to answer.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrl">The authorization to read.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The challenge to answer; <c>null</c> for an authorization that is already valid; or a typed failure.</returns>
    private static async Task<Result<OfferedChallenge?>> OfferedAsync(
        AcmeSession session,
        string authorizationUrl,
        CancellationToken cancellationToken)
    {
        var authorization = await session.PostAsync(authorizationUrl, AcmeProtocol.PostAsGet, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result<OfferedChallenge?>.Fail(
                Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
        }

        if (string.Equals(AcmeJson.Text(authorization.Value.Body, "status"), AuthorizationValid, StringComparison.Ordinal))
        {
            return Result<OfferedChallenge?>.Ok(null);
        }

        var challenge = ReadDnsChallenge(authorization.Value.Body);
        return challenge is null
            ? Result<OfferedChallenge?>.Fail(
                Error.Of(nameof(ErrorMessages.AcmeChallengeUnavailable), ErrorType.Unavailable))
            : Result<OfferedChallenge?>.Ok(challenge);
    }

    /// <summary>Words an authorization the authority judged unsuccessful.</summary>
    /// <param name="authorization">The authorization document.</param>
    /// <returns><c>AcmeDnsChallengeFailed</c> for a DNS, CAA or authorization problem, else <c>AcmeValidationFailed</c>.</returns>
    private Error WordInvalid(JsonElement authorization)
    {
        var problemType = ProblemTypeOf(authorization);
        LogChallengeRefused(_logger, problemType.Length == 0 ? "(none)" : problemType, null);

        return DnsProblemTypes.Contains(problemType, StringComparer.Ordinal)
            ? Error.Of(nameof(ErrorMessages.AcmeDnsChallengeFailed), ErrorType.UpstreamRefused)
            : Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.UpstreamRefused);
    }

    /// <summary>Removes one challenge record without letting its failure, or its throw, reach the order.</summary>
    /// <param name="handle">The record's receipt.</param>
    private async Task RemoveQuietlyAsync(DnsChallengeHandle handle)
    {
        try
        {
            var removed = await _writer.RemoveAsync(handle, CancellationToken.None);
            if (!removed.IsSuccess)
            {
                LogRemovalFailed(_logger, removed.Error!.Code, null);
            }
        }
        catch (Exception exception)
        {
            // Deliberately broad: see the remarks above. The order's own outcome is what the caller needs.
            LogRemovalFailed(_logger, exception.GetType().Name, exception);
        }
    }

    /// <summary>A DNS-01 challenge the authority offered for an authorization.</summary>
    /// <param name="Url">The challenge's URL, which is POSTed to ask the authority to look.</param>
    /// <param name="Token">The challenge's token, which the TXT value is derived from.</param>
    private sealed record OfferedChallenge(string Url, string Token);

    /// <summary>An authorization whose record is published and whose challenge is not yet answered.</summary>
    /// <param name="AuthorizationUrl">The authorization, polled after the challenge is answered.</param>
    /// <param name="ChallengeUrl">The challenge to answer.</param>
    private sealed record PendingChallenge(string AuthorizationUrl, string ChallengeUrl);
}
