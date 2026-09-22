using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Queries.GetLicenceStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

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
/// this endpoint normally, with <c>State: "Absent"</c> and 200 OK — never an error and never a 4xx,
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
    /// <summary>The handler this controller's one route dispatches to.</summary>
    /// <remarks>
    /// <b>Resolved directly, not through <c>IMessageBus</c>.</b> Every other module controller in this
    /// panel dispatches through Wolverine, but Wolverine's handler discovery walks
    /// <c>Maran.Host.Modules.ModuleRegistry.All</c> (<c>Maran.Host.Extensions.MessagingExtensions</c>'s
    /// own comment names this explicitly), and this module is deliberately NOT in that list — see
    /// <see cref="LicensingModule"/>'s own remarks for why registering it broke the SPA's composition
    /// check. A query routed through <c>IMessageBus</c> from an unregistered module's controller fails
    /// at runtime with "Could not determine any valid subscribers" (measured, not assumed, while
    /// writing this controller); resolving the handler through ordinary constructor injection instead
    /// sidesteps Wolverine entirely and needs no change to that registry.
    /// </remarks>
    private readonly GetLicenceStatusQueryHandler _handler;

    /// <summary>The handler the install route dispatches to.</summary>
    private readonly InstallLicenceCommandHandler _installHandler;

    /// <summary>Creates the controller with the caller identity and the handlers its routes dispatch to.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="handler">The handler the status route dispatches to.</param>
    /// <param name="installHandler">The handler the install route dispatches to.</param>
    public LicensingController(
        ICurrentUser currentUser,
        GetLicenceStatusQueryHandler handler,
        InstallLicenceCommandHandler installHandler)
        : base(currentUser)
    {
        _handler = handler;
        _installHandler = installHandler;
    }

    /// <summary>Reports the installed licence's current three-state status, changing nothing.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A <c>GET</c> because it performs no write: the licence artefact, if any, is re-read and
    /// re-verified, and the audit entry this call produces (see
    /// <see cref="Services.LicensingAuditJournal"/>) records that the fact was read, not that anything
    /// about the licence changed.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(LicenceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetLicenceStatusQuery(IpAddress: ClientIpAddress, UserAgent: CallerUserAgent);
        return ToActionResult(await _handler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Installs or replaces the licence artefact, verifying it first and persisting only on success.</summary>
    /// <param name="command">The uploaded licence text.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// <para>
    /// A <c>POST</c>, not a <c>PUT</c>: there is exactly one licence artefact and no caller-chosen
    /// identifier to address it by, the same reason the status route above takes none.
    /// </para>
    /// <para>
    /// A rejected upload changes nothing on disk — see
    /// <see cref="Commands.InstallLicence.InstallLicenceCommandHandler"/>'s own remarks for the
    /// verify-then-persist order this route relies on — and each of the five possible rejections
    /// answers its own distinguishable 400 code, never one generic "invalid licence".
    /// </para>
    /// <para>
    /// <b>Installing a licence does NOT bind it to this server.</b> The success response's
    /// <c>Sentence</c> field states this outright (see <c>Services.LicenceStatusDisplayNames.InstalledSentence</c>)
    /// — nothing in this build can check a server fingerprint yet
    /// (docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md §6).
    /// </para>
    /// </remarks>
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

        return ToActionResult(await _installHandler.HandleAsync(stamped, cancellationToken));
    }
}
