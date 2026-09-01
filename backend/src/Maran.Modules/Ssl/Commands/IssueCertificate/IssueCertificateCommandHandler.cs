using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Common.Interfaces;
using Maran.Modules.Ssl.Domain;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Commands.IssueCertificate;

/// <summary>
/// Handles <see cref="IssueCertificateCommand"/>: proves the caller owns the domain, orders a
/// certificate over HTTP-01, installs it, and only then records the row (spec §11).
/// </summary>
/// <remarks>
/// The order of the steps is the whole design.
///
/// Ownership is established FIRST, through the tenant-scoped site directory. A caller who names a
/// domain hosted here by somebody else gets the same answer as one who names a domain that is not
/// hosted here at all — a certificate is issuance authority over a name, and confirming that a name
/// exists on this server would be the first half of taking it (rules/security.md — 404, never 403).
///
/// The ORDER comes second, and no row is written before it succeeds. A failed order must leave no
/// trace in the certificates table: a row for a certificate that does not exist would make the site
/// look protected in the interface, and would make renewal try to renew nothing.
///
/// The INSTALL comes third and the row LAST, so the row can never describe material that is not on
/// disk. The reverse — a row written before the agent ran — is the case where the panel tells a
/// customer their site has TLS and the site answers on port 80.
/// </remarks>
public sealed class IssueCertificateCommandHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The tenant-scoped window onto the sites this caller owns.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The owning account's system user name, which every agent operation is addressed by.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The certificate authority.</summary>
    private readonly IAcmeClient _acme;

    /// <summary>The one path that puts material on the host and marks the site as carrying it.</summary>
    private readonly CertificateInstaller _installer;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="sites">The tenant-scoped window onto the sites this caller owns.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="acme">The certificate authority.</param>
    /// <param name="installer">The shared install path.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    public IssueCertificateCommandHandler(
        SslDbContext dbContext,
        ISiteDirectory sites,
        IAccountDirectory accounts,
        IAcmeClient acme,
        CertificateInstaller installer,
        CertificateAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _sites = sites;
        _accounts = accounts;
        _acme = acme;
        _installer = installer;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Issues and installs a certificate for one of the caller's sites.</summary>
    /// <param name="command">The validated domain; see <see cref="IssueCertificateCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded certificate, or <c>SiteNotFound</c>, <c>CertificateAlreadyIssued</c>,
    /// <c>AccountNotFound</c>, or the authority's or agent's own typed failure.
    /// </returns>
    public async Task<Result<CertificateDto>> HandleAsync(
        IssueCertificateCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for a site this caller does not own, so a guessed
        // domain reads as "not found" rather than "forbidden".
        var site = await _sites.FindByDomainAsync(command.Domain, cancellationToken);
        if (site is null)
        {
            return await FailAsync(command, nameof(ErrorMessages.SiteNotFound), cancellationToken);
        }

        // Deliberately ignores the tenant filter: a domain carries one certificate across the whole
        // server, so a certificate already issued for this domain under ANOTHER account still blocks
        // it. Without this the filter would hide the row, the check would pass, and the insert would
        // fail on the unique index as an unhandled exception instead of a typed 409.
        var alreadyIssued = await _dbContext.Certificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(certificate => certificate.Domain == command.Domain, cancellationToken);
        if (alreadyIssued)
        {
            return await FailAsync(command, nameof(ErrorMessages.CertificateAlreadyIssued), cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, nameof(ErrorMessages.AccountNotFound), cancellationToken);
        }

        var issued = await _acme.OrderAsync(
            new AcmeOrderRequest(command.Domain, account.Username), cancellationToken);
        if (!issued.IsSuccess)
        {
            return await FailAsync(command, issued.Error!.Code, cancellationToken);
        }

        var installed = await _installer.InstallAsync(
            account.Username, site, issued.Value.CertificatePem, issued.Value.PrivateKeyPem, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, installed.Error!.Code, cancellationToken);
        }

        var certificate = new Certificate(
            Guid.NewGuid(),
            site.AccountId,
            site.Id,
            command.Domain,
            CertificateSource.Acme,
            installed.Value,
            _clock.UtcNow);

        _dbContext.Certificates.Add(certificate);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The check above and this insert are not one atomic step, so two simultaneous requests
            // for the same domain can both pass the check; the unique index on Domain is what stops
            // the second, and this is where that arrives.
            //
            // Caught rather than left to surface as a 500, because the two stores must not be left
            // disagreeing. By this point the agent HAS installed material for the domain and the site
            // row already says it carries a certificate — and both of those are still TRUE, because
            // the winner installed for the same domain into the same slot. So the flag is correct and
            // is deliberately NOT cleared; what is missing is only this caller's duplicate row, and
            // the winner's row is the one renewal will use. The caller is told the domain is taken.
            _dbContext.Certificates.Remove(certificate);
            return await FailAsync(command, nameof(ErrorMessages.CertificateAlreadyIssued), cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CertificateIssued, command.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CertificateDto>.Ok(CertificateDtoFactory.From(certificate));
    }

    /// <summary>Journals a refused issuance and returns it as the typed failure.</summary>
    /// <param name="command">The issuance that was refused, whose domain is the journal's subject.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<CertificateDto>> FailAsync(
        IssueCertificateCommand command,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CertificateIssued, command.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CertificateDto>.Fail(Error.Of(code));
    }
}
