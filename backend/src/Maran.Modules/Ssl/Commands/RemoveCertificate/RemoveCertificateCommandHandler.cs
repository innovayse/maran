using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Commands.RemoveCertificate;

/// <summary>
/// Handles <see cref="RemoveCertificateCommand"/>: takes the material off the host, returns the site
/// to plain HTTP, and deletes the row (spec §11).
/// </summary>
/// <remarks>
/// The reads are tenant-scoped, so a certificate belonging to another customer is simply not found —
/// 404 and never 403, because a 403 would confirm that the certificate exists (spec §8).
///
/// The order is the mirror of installation and is chosen for the same reason: the agent runs FIRST
/// and the row is deleted only if it succeeded. A row deleted while the material is still installed
/// leaves the panel unable to renew a certificate that is still serving, and the site goes dark on a
/// date nothing is watching. A vhost returned to HTTP whose row survives is visible, wrong in the
/// safe direction, and fixed by removing again.
///
/// The site's certificate flag is cleared through the site directory, because a vhost re-rendered
/// later from a row that still says "certificate" would write a TLS block pointing at files that are
/// gone — which nginx refuses to load, taking the site down rather than merely leaving it on HTTP.
/// </remarks>
public sealed class RemoveCertificateCommandHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The window onto the sites this caller owns, and the hand on the certificate flag.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The owning account's system user name, which every agent operation is addressed by.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the certificate store and the vhost.</summary>
    private readonly IAgentSslClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="sites">The window onto the sites this caller owns.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the material and rewrites the vhost.</param>
    /// <param name="journal">This module's audit journal.</param>
    public RemoveCertificateCommandHandler(
        SslDbContext dbContext,
        ISiteDirectory sites,
        IAccountDirectory accounts,
        IAgentSslClient agent,
        CertificateAuditJournal journal)
    {
        _dbContext = dbContext;
        _sites = sites;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes one of the caller's certificates.</summary>
    /// <param name="command">The certificate to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c>, or <c>CertificateNotFound</c>, <c>SiteNotFound</c>, or the agent's failure.</returns>
    public async Task<Result<bool>> HandleAsync(
        RemoveCertificateCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-filtered by the context, so another customer's certificate is not found here.
        var certificate = await _dbContext.Certificates
            .FirstOrDefaultAsync(candidate => candidate.Id == command.Id, cancellationToken);
        if (certificate is null)
        {
            return await FailAsync(
                command.Id.ToString(), Error.Of(nameof(ErrorMessages.CertificateNotFound), ErrorType.NotFound), command, cancellationToken);
        }

        var site = await _sites.FindByDomainAsync(certificate.Domain, cancellationToken);
        if (site is null)
        {
            return await FailAsync(
                certificate.Domain, Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound), command, cancellationToken);
        }

        var account = await _accounts.FindAsync(certificate.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                certificate.Domain, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), command, cancellationToken);
        }

        var removed = await _agent.RemoveCertificateAsync(
            account.Username,
            certificate.Domain,
            SiteDescriptorMapper.From(site, hasCertificate: false),
            cancellationToken);
        if (!removed.IsSuccess)
        {
            return await FailAsync(certificate.Domain, removed.Error!, command, cancellationToken);
        }

        // The ROW's own linkage, not the site the domain lookup happened to return. The two agree
        // today because Site.Domain is unique across the server, but the certificate row is what
        // records which site this material was installed for, and a lookup by domain is one rename
        // away from disagreeing with it.
        await _sites.DetachCertificateAsync(certificate.SiteId, cancellationToken);

        _dbContext.Certificates.Remove(certificate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.CertificateRemoved,
            certificate.Domain,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused removal and returns it as the typed failure.</summary>
    /// <param name="subject">
    /// The domain where one is known; otherwise the identifier the caller supplied, so a probe for a
    /// certificate the caller may not see still leaves a trace naming what was probed for.
    /// </param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="command">The removal that was refused.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        string subject,
        Error error,
        RemoveCertificateCommand command,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CertificateRemoved, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
