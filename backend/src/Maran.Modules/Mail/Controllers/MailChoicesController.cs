using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.GetMailChoices;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// The reference data the mail screens select from and the bounds their forms validate against, in the
/// caller's language and for the caller's plan.
/// </summary>
[Route("api/v1/mail/choices")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Mail")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Mail)]
public sealed class MailChoicesController : BaseApiController
{
    /// <summary>Dispatches queries through Wolverine.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Who is calling.</param>
    /// <param name="bus">The message bus the query is dispatched on.</param>
    public MailChoicesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Returns the statuses, alias kinds and numeric bounds.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The choices.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(MailChoicesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<MailChoicesDto>>(new GetMailChoicesQuery(), cancellationToken));
    }
}
