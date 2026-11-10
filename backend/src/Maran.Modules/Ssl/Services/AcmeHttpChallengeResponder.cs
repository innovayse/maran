using System.Globalization;
using System.Text.Json;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Proves control of the order's name over HTTP-01: places the challenge file, asks the authority to read
/// it, and waits, bounded, for the authority to accept.
/// </summary>
internal sealed class AcmeHttpChallengeResponder : IAcmeChallengeResponder
{
    /// <summary>The authorization status meaning "control of this name is already proven".</summary>
    private const string AuthorizationValid = "valid";

    /// <summary>Places and removes the challenge file, through the agent, as the account.</summary>
    private readonly IAcmeChallengeWriter _challengeWriter;

    /// <summary>Waits for the authority's judgement.</summary>
    private readonly AcmeAuthorizationPoller _poller;

    /// <summary>Creates the responder.</summary>
    /// <param name="challengeWriter">Places and removes the HTTP-01 challenge file.</param>
    /// <param name="poller">Waits for the authority's judgement.</param>
    public AcmeHttpChallengeResponder(IAcmeChallengeWriter challengeWriter, AcmeAuthorizationPoller poller)
    {
        _challengeWriter = challengeWriter;
        _poller = poller;
    }

    /// <inheritdoc />
    public async Task<Result> RespondAsync(
        AcmeSession session,
        IReadOnlyList<string> authorizationUrls,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var authorizationUrl in authorizationUrls)
        {
            var answered = await AnswerAsync(session, authorizationUrl, request, cancellationToken);
            if (!answered.IsSuccess)
            {
                return answered;
            }
        }

        return Result.Ok();
    }

    /// <summary>Builds the HTTP-01 key authorization: the token joined to the account key's thumbprint.</summary>
    /// <param name="token">The challenge token from the authority.</param>
    /// <param name="thumbprint">The account key's RFC 7638 thumbprint.</param>
    /// <returns>Exactly the bytes the authority will fetch and compare.</returns>
    private static string KeyAuthorization(string token, string thumbprint)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{token}.{thumbprint}");
    }

    /// <summary>Finds the HTTP-01 challenge among the ones the authority offered.</summary>
    /// <param name="authorization">The authorization object.</param>
    /// <param name="challengeUrl">The challenge's URL, when one was found.</param>
    /// <param name="token">The challenge's token, when one was found.</param>
    /// <returns><c>true</c> when an HTTP-01 challenge is present with both fields.</returns>
    private static bool TryReadHttpChallenge(JsonElement authorization, out string challengeUrl, out string token)
    {
        challengeUrl = string.Empty;
        token = string.Empty;

        if (!authorization.TryGetProperty("challenges", out var challenges)
            || challenges.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var challenge in challenges.EnumerateArray())
        {
            if (!string.Equals(AcmeJson.Text(challenge, "type"), "http-01", StringComparison.Ordinal))
            {
                continue;
            }

            challengeUrl = AcmeJson.Text(challenge, "url");
            token = AcmeJson.Text(challenge, "token");
            return challengeUrl.Length > 0 && token.Length > 0;
        }

        return false;
    }

    /// <summary>Answers the HTTP-01 challenge of one authorization and waits for the authority to accept it.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrl">The authorization to satisfy.</param>
    /// <param name="request">The domain and where its challenge file is placed.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Success once the authorization is valid, or a typed failure.</returns>
    /// <remarks>
    /// The challenge file is removed in a <c>finally</c>, and its removal can never turn a successful
    /// validation into a failure: a consumed token proves nothing to anybody, while discarding a
    /// valid authorization over a failed unlink would cost a real certificate.
    /// </remarks>
    private async Task<Result> AnswerAsync(
        AcmeSession session,
        string authorizationUrl,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var authorization = await session.PostAsync(authorizationUrl, AcmeProtocol.PostAsGet, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
        }

        // The same cached-authorization case as the order status above, checked separately because
        // the two are not redundant: an order can be "pending" while one of its authorizations is
        // already "valid" (a multi-identifier order, or an authority that has not yet recomputed the
        // order). Answering a challenge on a valid authorization is refused as "malformed".
        if (string.Equals(AcmeJson.Text(authorization.Value.Body, "status"), AuthorizationValid, StringComparison.Ordinal))
        {
            return Result.Ok();
        }

        if (!TryReadHttpChallenge(authorization.Value.Body, out var challengeUrl, out var token))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.AcmeChallengeUnavailable), ErrorType.Unavailable));
        }

        var written = await _challengeWriter.WriteAsync(
            request.Challenge,
            request.Domain,
            token,
            KeyAuthorization(token, session.Thumbprint),
            cancellationToken);
        if (!written.IsSuccess)
        {
            return Result.Fail(written.Error!); // the writer already worded it (floor kept as itself)
        }

        try
        {
            var triggered = await session.PostAsync(challengeUrl, "{}", cancellationToken);
            if (!triggered.IsSuccess)
            {
                return Result.Fail(
                    Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.UpstreamRefused));
            }

            return await _poller.PollAsync(
                session,
                authorizationUrl,
                _ =>
                {
                    return Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.UpstreamRefused);
                },
                cancellationToken);
        }
        finally
        {
            // Deliberately unchecked: see the remarks above. The agent logs its own failure.
            await _challengeWriter.RemoveAsync(
                request.Challenge, request.Domain, token, CancellationToken.None);
        }
    }
}
