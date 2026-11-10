using System.Text.Json;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Services;

/// <summary>Polls an authorization until the authority has judged it, within a bound the injected clock keeps.</summary>
internal sealed class AcmeAuthorizationPoller
{
    /// <summary>The bounds of every wait.</summary>
    private readonly AcmeOptions _options;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the poller.</summary>
    /// <param name="options">The bounds of every wait.</param>
    /// <param name="clock">The injected time source, which bounds the poll.</param>
    public AcmeAuthorizationPoller(AcmeOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    /// <summary>Polls one authorization until it is valid, invalid, or the bound is reached.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrl">The authorization to poll.</param>
    /// <param name="whenInvalid">
    /// Words an authorization the authority judged unsuccessful, given the authorization document so the
    /// challenge type that was answered can decide the code. Only its machine fields may be read.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Success when valid; <paramref name="whenInvalid"/>'s error, <c>AcmeValidationFailed</c> or <c>AcmeValidationTimedOut</c> otherwise.</returns>
    /// <remarks>
    /// The deadline is computed from the injected clock before the first poll, so the wait is bounded
    /// by construction rather than by hoping the authority eventually answers something terminal. An
    /// unbounded poll here is not a slow renewal, it is a renewal job that never returns and a
    /// certificate that silently stops being renewed.
    /// </remarks>
    public async Task<Result> PollAsync(
        AcmeSession session,
        string authorizationUrl,
        Func<JsonElement, Error> whenInvalid,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + _options.ValidationTimeout;

        while (_clock.UtcNow < deadline)
        {
            await Task.Delay(_options.PollInterval, cancellationToken);

            var polled = await session.PostAsync(authorizationUrl, AcmeProtocol.PostAsGet, cancellationToken);
            if (!polled.IsSuccess)
            {
                return Result.Fail(
                    Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.UpstreamRefused));
            }

            var status = AcmeJson.Text(polled.Value.Body, "status");
            if (string.Equals(status, "valid", StringComparison.Ordinal))
            {
                return Result.Ok();
            }

            if (string.Equals(status, "invalid", StringComparison.Ordinal)
                || string.Equals(status, "revoked", StringComparison.Ordinal)
                || string.Equals(status, "deactivated", StringComparison.Ordinal))
            {
                return Result.Fail(whenInvalid(polled.Value.Body));
            }
        }

        return Result.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationTimedOut), ErrorType.Unavailable));
    }
}
