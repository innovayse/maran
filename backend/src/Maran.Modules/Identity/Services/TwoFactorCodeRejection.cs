using Maran.Modules.Identity.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one answer every endpoint gives when it refuses a two-factor code: the plain refusal, or —
/// when the latest monitoring reading found the host's clock not synchronised — the same refusal
/// saying the clock may be why (issue #150).
/// </summary>
/// <remarks>
/// <para>
/// A time-based code is computed from the clock, so a server whose clock has drifted refuses every
/// correct code, and the person holding one has no way to tell that from typing it wrong. The hint
/// changes only the sentence: the code is still refused, with the same kind and the same status.
/// </para>
/// <para>
/// One type rather than a branch in each handler, because three endpoints refuse a code — sign-in,
/// enrolment and switching the factor off — and a hint present on one and missing on another is the
/// drift a single answer prevents. Each of them reaches it only after the caller's identity is
/// established, so the hint tells nobody without the password anything about this server.
/// </para>
/// </remarks>
public sealed class TwoFactorCodeRejection
{
    /// <summary>Reads whether the latest monitoring round found the clock not synchronised.</summary>
    private readonly IClockSyncStatus _clock;

    /// <summary>Creates the rejection.</summary>
    /// <param name="clock">The window onto the latest clock reading.</param>
    public TwoFactorCodeRejection(IClockSyncStatus clock)
    {
        _clock = clock;
    }

    /// <summary>Builds the error a refused two-factor code is answered with.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// <c>InvalidTwoFactorCodeClockUnsynchronizedUnauthorized</c> when the clock was found not
    /// synchronised, otherwise <c>InvalidTwoFactorCodeUnauthorized</c>; both are
    /// <see cref="ErrorType.Unauthorized"/>.
    /// </returns>
    public async Task<Error> ErrorAsync(CancellationToken cancellationToken)
    {
        var code = await _clock.IsClockUnsynchronizedAsync(cancellationToken)
            ? nameof(ErrorMessages.InvalidTwoFactorCodeClockUnsynchronizedUnauthorized)
            : nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized);

        return Error.Of(code, ErrorType.Unauthorized);
    }
}
