using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Queries.GetLicenceStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Licensing.Controllers;

/// <summary>
/// HTTP surface for the installed licence: its status (spec §228, read-only) and, per
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c>, installing or
/// replacing the artefact.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>AdminOnly</c> on the class, not <c>AnyAuthenticated</c>, and the argument follows
/// <c>Databases.Controllers.DatabaseGrantsController</c>'s own shape exactly.</b> A licence
/// status names the product, the tier and the expiry of the WHOLE installation — an operator's fact,
/// not a customer's. Nothing about it is scoped to the caller's own account, so there is no narrower
/// answer an <c>AnyAuthenticated</c> caller could safely be given; the policy on this class IS the
/// authorisation, the same way it is on that controller.
/// </para>
/// <para>
/// <b>Refuses with 403, never 404.</b> A 404 exists to stop an identifier from being used as an
/// oracle — "does this row exist" leaking through the shape of the refusal. There is no identifier
/// here: one installation has exactly one licence status, and it plainly exists (even
/// <see cref="Domain.Enums.LicenceStatus.Absent"/> is an answer, not a missing resource), so a 404
/// would be a lie about a fact this server can always state. 403 says the true thing: the caller may
/// not read it.
/// </para>
/// <para>
/// <b>§229 still governs this read.</b> <c>docs/superpowers/notes/2026-09-22-licence-verification-
/// threat-note.md</c> §2: "деградируют только платные модули; ядро не умирает никогда." The handler
/// behind this route re-verifies through <see cref="Services.LicenceVerifier"/>, whose
/// <c>VerifyAsync</c> is typed to never throw and to never produce anything but the three-state
/// result — this controller adds no <c>try/catch</c> and no exception-to-error mapping of its own, so
/// there is no second path here for a future edit to forget to guard. An unlicensed install answers
/// this endpoint normally, with a state of <c>absent</c> and 200 OK — never an error and never a 4xx,
/// exactly as an ordinary first-run state should read.
/// </para>
/// <para>
/// <b>What the response may not carry</b> — see <see cref="LicenceStatusDto"/>'s own remarks for the
/// full argument: never the licence's signature material, never a server fingerprint's raw inputs. A
/// licence id is safe to return; those are not. The install route below inherits this rule too: it
/// never echoes the uploaded bytes back, on success or refusal.
/// </para>
/// <para>
/// <b>The install route carries the identical <c>AdminOnly</c> policy and the identical §229
/// guarantee.</b> It is one more reason the class-level policy exists rather than a per-route one:
/// there is no narrower answer a customer could safely be given for either route, and a caller who
/// uploads rubbish is refused with a specific, distinguishable 400 — never a 500 and never a crash of
/// anything else the panel is doing (see <see cref="Commands.InstallLicence.InstallLicenceCommandHandler"/>'s
/// own remarks).
/// </para>
/// </remarks>
[Route("api/v1/licence-status")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Licensing")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class LicensingController : BaseApiController
{
    /// <summary>The bus the query and the command are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The bus the query and the command are dispatched through.</param>
    public LicensingController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the installed licence's status and the standing of its lease.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with the status; 401 or 403 for a caller who is not an administrator.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(LicenceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetLicenceStatusQuery(IpAddress: ClientIpAddress, UserAgent: CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<LicenceStatusDto>>(query, cancellationToken));
    }

    /// <summary>Verifies an uploaded licence and installs it only when it verifies.</summary>
    /// <param name="command">The uploaded licence text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with the new status; 400 with a distinguishable code when the licence is refused.</returns>
    [HttpPost("install")]
    [ProducesResponseType(typeof(LicenceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InstallAsync(
        [FromBody] InstallLicenceCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<LicenceStatusDto>>(stamped, cancellationToken));
    }
}
