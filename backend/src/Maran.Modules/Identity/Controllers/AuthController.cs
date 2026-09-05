using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Commands.BeginTotpEnrolment;
using Maran.Modules.Identity.Commands.ConfirmTotpEnrolment;
using Maran.Modules.Identity.Commands.DisableTotp;
using Maran.Modules.Identity.Commands.Login;
using Maran.Modules.Identity.Commands.Logout;
using Maran.Modules.Identity.Commands.LogoutEverywhere;
using Maran.Modules.Identity.Commands.RefreshSession;
using Maran.Modules.Identity.Commands.RequestPasswordReset;
using Maran.Modules.Identity.Commands.ResetPassword;
using Maran.Modules.Identity.Commands.VerifyTwoFactor;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Controllers.Requests;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The panel's sign-in surface. Thin by design (rules/csharp.md "Controller shape is fixed"): binds
/// the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>, and — the one
/// thing only it can do — moves the refresh token into its cookie.
///
/// Anonymous access is granted per action rather than on the class. A class-level
/// <c>[AllowAnonymous]</c> outranks an action-level <c>[Authorize]</c> — it does not merge with it —
/// so <c>logout-all</c>, the one action here that must know who is asking, would have been open to
/// anyone. The analyzer caught it; the shape that cannot go wrong is to name each anonymous action.
/// </summary>
[Route("api/v1/auth")]
[Tags("Auth")]
[Produces("application/json")]
public sealed class AuthController : BaseApiController
{
    /// <summary>The message bus commands are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal, anonymous on this controller.</param>
    /// <param name="bus">The message bus commands are dispatched through.</param>
    public AuthController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Signs a user in. On success the access token is returned in the body and the refresh token
    /// is set as an httpOnly cookie; when the user has a second factor, neither is issued yet.
    /// </summary>
    /// <remarks>
    /// Rate limited per (address, username) — mandatory on authentication (rules/security.md). The
    /// limiter's partition resolver runs before the body can be read, so the SPA repeats the name in
    /// the query string purely to partition the limiter; the credential checked is always the one in
    /// the body. A caller who omits or forges the query value only makes their own attempts share a
    /// coarser bucket, never a larger budget.
    /// </remarks>
    /// <param name="request">The username and password.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Username, request.Password, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<LoginOutcome>>(command, cancellationToken);

        if (result.IsSuccess && result.Value.Authenticated is { } authenticated)
        {
            RefreshCookie.Append(Response, authenticated.Session);
        }

        return ToActionResult(result.Match(
            outcome =>
            {
                return Result<LoginResultDto>.Ok(new LoginResultDto(
                    outcome.Authenticated is { } signedIn
                        ? AuthenticatedSessionMapper.From(signedIn)
                        : null));
            },
            error =>
            {
                return Result<LoginResultDto>.Fail(error);
            }));
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token, rotating the cookie. Anonymous by
    /// design: the caller's access token has usually expired by the time they get here, which is
    /// the whole reason this endpoint exists — the refresh cookie is the credential.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticatedSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookie.Name];
        if (string.IsNullOrEmpty(refreshToken))
        {
            // No cookie at all is a plain "sign in again", not a server fault: a first-time visitor
            // and a visitor whose cookie expired are the same case, and both belong on the login
            // screen rather than looking at a 500.
            return ToActionResult(Result<AuthenticatedSessionDto>.Fail(
                Error.Of(nameof(Resources.ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized)));
        }

        var command = new RefreshSessionCommand(refreshToken, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AuthenticatedOutcome>>(command, cancellationToken);

        if (result.IsSuccess)
        {
            // A rotation always issues a session, so there is nothing to test but success: the type
            // no longer admits a rotated outcome without one.
            RefreshCookie.Append(Response, result.Value.Session);
        }
        else
        {
            // A refused refresh means this cookie will never work again — a rotated one, an expired
            // one, or one whose whole family was just revoked. Leaving it in the browser only makes
            // the next page load fail the same way.
            RefreshCookie.Delete(Response);
        }

        return ToActionResult(result.Match(
            outcome =>
            {
                return Result<AuthenticatedSessionDto>.Ok(AuthenticatedSessionMapper.From(outcome));
            },
            error =>
            {
                return Result<AuthenticatedSessionDto>.Fail(error);
            }));
    }

    /// <summary>Signs the caller out of this device.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookie.Name];
        RefreshCookie.Delete(Response);

        if (string.IsNullOrEmpty(refreshToken))
        {
            return ToActionResult(Result<bool>.Ok(true));
        }

        var command = new LogoutCommand(refreshToken, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Signs the caller out of every device.</summary>
    /// <remarks>
    /// The only endpoint here that requires a valid access token: it acts on every session a person
    /// has, so it must be certain who is asking — a refresh cookie alone would let a stolen cookie
    /// lock its owner out of their own panel.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutEverywhereAsync(CancellationToken cancellationToken)
    {
        RefreshCookie.Delete(Response);

        var command = new LogoutEverywhereCommand(CurrentUser.UserId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Finishes a sign-in that stopped for a second factor. Anonymous, and it re-checks the
    /// password: this endpoint is reachable on its own, so treating the caller as already half
    /// authenticated would make the code the only factor for anyone who calls it directly.
    /// </summary>
    /// <param name="request">Both factors.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(AuthenticatedSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyTwoFactorAsync(
        [FromBody] VerifyTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        var command = new VerifyTwoFactorCommand(
            request.Username, request.Password, request.Code, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<AuthenticatedOutcome>>(command, cancellationToken);

        if (result.IsSuccess)
        {
            // Verifying the second factor either signs the caller in or fails; a success carries a
            // session by construction, so there is no "signed in without a cookie" branch left.
            RefreshCookie.Append(Response, result.Value.Session);
        }

        return ToActionResult(result.Match(
            outcome =>
            {
                return Result<AuthenticatedSessionDto>.Ok(AuthenticatedSessionMapper.From(outcome));
            },
            error =>
            {
                return Result<AuthenticatedSessionDto>.Fail(error);
            }));
    }

    /// <summary>Starts a two-factor enrolment: returns a secret, enables nothing.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("two-factor/enrol")]
    [Authorize]
    [AllowDuringTwoFactorEnrolment]
    [ProducesResponseType(typeof(TotpEnrolmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BeginTwoFactorEnrolmentAsync(CancellationToken cancellationToken)
    {
        var command = new BeginTotpEnrolmentCommand(CurrentUser.UserId);
        return ToActionResult(await _bus.InvokeAsync<Result<TotpEnrolmentDto>>(command, cancellationToken));
    }

    /// <summary>Completes an enrolment and returns the recovery codes, once.</summary>
    /// <param name="request">The secret and a code proving it works.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("two-factor/confirm")]
    [Authorize]
    [AllowDuringTwoFactorEnrolment]
    [ProducesResponseType(typeof(RecoveryCodesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmTwoFactorEnrolmentAsync(
        [FromBody] ConfirmTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ConfirmTotpEnrolmentCommand(
            CurrentUser.UserId, request.Secret, request.Code, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<RecoveryCodesDto>>(command, cancellationToken));
    }

    /// <summary>Turns the second factor off, for a caller who can still satisfy it.</summary>
    /// <param name="request">A current code or a recovery code.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("two-factor/disable")]
    [Authorize]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisableTwoFactorAsync(
        [FromBody] DisableTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        var command = new DisableTotpCommand(
            CurrentUser.UserId, request.Code, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Asks for a password-reset link. Answers the same way whether or not the address belongs to an
    /// account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The response is deliberately uninformative, and that is the feature.</b> Status, body and —
    /// as far as this panel can make it so — timing are identical for an address that exists and one
    /// that does not, because any difference between them is a way to test whether somebody holds an
    /// account here. The handler is where that is enforced; see
    /// <see cref="RequestPasswordResetCommandHandler"/>.
    /// </para>
    /// <para>
    /// <b>Rate limited on its own bucket</b>, not the login one. What this endpoint spends is an
    /// outgoing message with the operator's return address on it, and an unlimited one is a mail
    /// bomb aimed at whatever address the caller names.
    /// </para>
    /// </remarks>
    /// <param name="request">The address to send the link to.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordResetAsync(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RequestPasswordResetCommand(request.Email, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Sets a new password from a reset link, and signs the account out everywhere.</summary>
    /// <remarks>
    /// Anonymous by necessity: the caller has forgotten their password, and the token IS the
    /// credential. It is single-use and expires in an hour, and a token that never existed, has
    /// expired, or has already been spent all get the same refusal — a caller must not learn which.
    /// </remarks>
    /// <param name="request">The token from the mail and the new password.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordReset)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(
            request.Token, request.NewPassword, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
