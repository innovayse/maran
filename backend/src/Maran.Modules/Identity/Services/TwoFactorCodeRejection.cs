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
/// drift a single answer prevents. Two kinds of answer, because the kind is decided by who is
/// asking: a sign-in has no session yet and its refusal is a 401, while enrolment and switching off
/// are made by a caller whose session was accepted, so their refusal is a 400. Each of them reaches
/// it only after the caller's identity is established, so the hint tells nobody without the
/// password anything about this server.
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

    /// <summary>Builds the error a refused code is answered with when it is the credential of a sign-in.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// <c>InvalidTwoFactorCodeClockUnsynchronizedUnauthorized</c> when the clock was found not
    /// synchronised, otherwise <c>InvalidTwoFactorCodeUnauthorized</c>; both are
    /// <see cref="ErrorType.Unauthorized"/>: the caller has not signed in yet, and the code is the
    /// thing that would have signed them in.
    /// </returns>
    public async Task<Error> SignInErrorAsync(CancellationToken cancellationToken)
    {
        var code = await _clock.IsClockUnsynchronizedAsync(cancellationToken)
            ? nameof(ErrorMessages.InvalidTwoFactorCodeClockUnsynchronizedUnauthorized)
            : nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized);

        return Error.Of(code, ErrorType.Unauthorized);
    }

    /// <summary>
    /// Builds the error a refused code is answered with when the caller is ALREADY signed in and the
    /// code proves a change to their own factor (enrolling it, or turning it off).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// <c>TwoFactorCodeClockUnsynchronized</c> when the clock was found not synchronised, otherwise
    /// <c>TwoFactorCodeInvalid</c>; both are <see cref="ErrorType.Validation"/> (400). Not 401: the
    /// access token was accepted, the caller's identity is established, and what they typed is a
    /// value they could have typed differently. A 401 here would tell every client that the session
    /// is the problem, and a client that renews a session and replays on a 401 would send the same
    /// wrong code twice (issue #412).
    /// </returns>
    public async Task<Error> ProofErrorAsync(CancellationToken cancellationToken)
    {
        var code = await _clock.IsClockUnsynchronizedAsync(cancellationToken)
            ? nameof(ErrorMessages.TwoFactorCodeClockUnsynchronized)
            : nameof(ErrorMessages.TwoFactorCodeInvalid);

        return Error.Of(code, ErrorType.Validation);
    }
}
