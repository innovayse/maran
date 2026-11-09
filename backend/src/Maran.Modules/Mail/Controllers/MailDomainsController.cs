using Maran.Modules.Mail.Commands.CreateMailDomain;
using Maran.Modules.Mail.Commands.DeleteMailDomain;
using Maran.Modules.Mail.Commands.RotateDkimKey;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.GetMailDomainDnsRecords;
using Maran.Modules.Mail.Queries.ListMailDomains;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// An account's mail domains: listing, adding a site's domain, deleting one with everything under it,
/// rotating its DKIM key and reading the DNS records it needs. Account scope is the query filter, so
/// another account's domain answers 404.
/// </summary>
[Route("api/v1/mail-domains")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Mail Domains")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Mail)]
public sealed class MailDomainsController : BaseApiController
{
    /// <summary>Dispatches commands and queries through Wolverine.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Who is calling.</param>
    /// <param name="bus">The message bus commands and queries are dispatched on.</param>
    public MailDomainsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists mail domains with search, filters, sorting and paging.</summary>
    /// <param name="query">Filter, sort and paging parameters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of domains.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<MailDomainDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListMailDomainsQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<MailDomainDto>>>(query, cancellationToken));
    }

    /// <summary>Adds a site's domain as a mail domain.</summary>
    /// <param name="command">The owning account and the domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created domain.</returns>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailDomainDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateMailDomainCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<MailDomainDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/mail-domains/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Deletes a mail domain with its mailboxes, aliases, autoresponders and keys.</summary>
    /// <param name="id">The domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteMailDomainCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Replaces the domain's DKIM signing key, keeping the previous one published while it retires.</summary>
    /// <param name="id">The domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The domain with its new selector.</returns>
    [HttpPost("{id:guid}/dkim-rotation")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailDomainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateDkimKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RotateDkimKeyCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<MailDomainDto>>(command, cancellationToken));
    }

    /// <summary>Returns the DNS records the domain needs.</summary>
    /// <param name="id">The domain.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The MX, SPF, DKIM and DMARC records.</returns>
    [HttpGet("{id:guid}/dns-records")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<MailDnsRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetDnsRecordsAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<MailDnsRecordDto>>>(
            new GetMailDomainDnsRecordsQuery(id), cancellationToken);
        return ToActionResult(result);
    }
}
