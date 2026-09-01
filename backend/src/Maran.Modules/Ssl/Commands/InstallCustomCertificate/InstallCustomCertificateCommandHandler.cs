using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Commands.InstallCustomCertificate;

/// <summary>
/// Handles <see cref="InstallCustomCertificateCommand"/>: installs material the customer supplied on
/// one of their own sites (spec §11).
/// </summary>
/// <remarks>
/// The same shape as issuance minus the order, and with one difference that matters: an existing
/// certificate is REPLACED rather than refused. Replacing is the whole point of the operation — a
/// customer whose certificate is about to expire uploads the new one — whereas issuance refuses a
/// domain that already has a certificate, because ordering a second one from an authority spends
/// rate-limit budget to arrive at the state the domain is already in.
///
/// The material never reaches the database, the journal or a log. It is an argument to the agent and
/// then it is gone; what is recorded is the certificate's expiry, which the AGENT parsed out of the
/// installed file rather than something this handler was told (rules/security.md item 8).
/// </remarks>
public sealed class InstallCustomCertificateCommandHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The tenant-scoped window onto the sites this caller owns.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The owning account's system user name, which every agent operation is addressed by.</summary>
    private readonly IAccountDirectory _accounts;

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
    /// <param name="installer">The shared install path.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the row.</param>
    public InstallCustomCertificateCommandHandler(
        SslDbContext dbContext,
        ISiteDirectory sites,
        IAccountDirectory accounts,
        CertificateInstaller installer,
        CertificateAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _sites = sites;
        _accounts = accounts;
        _installer = installer;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Installs the supplied material for one of the caller's sites.</summary>
    /// <param name="command">The validated material and domain.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recorded certificate, or <c>SiteNotFound</c>, <c>AccountNotFound</c>, or the agent's failure.</returns>
    public async Task<Result<CertificateDto>> HandleAsync(
        InstallCustomCertificateCommand command,
        CancellationToken cancellationToken)
    {
        var site = await _sites.FindByDomainAsync(command.Domain, cancellationToken);
        if (site is null)
        {
            return await FailAsync(command, nameof(ErrorMessages.SiteNotFound), cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, nameof(ErrorMessages.AccountNotFound), cancellationToken);
        }

        var installed = await _installer.InstallAsync(
            account.Username, site, command.CertificatePem, command.PrivateKeyPem, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, installed.Error!.Code, cancellationToken);
        }

        var certificate = await UpsertAsync(command.Domain, site, installed.Value, cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.CertificateInstalled,
            command.Domain,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<CertificateDto>.Ok(CertificateDtoFactory.From(certificate));
    }

    /// <summary>Replaces the domain's existing row, or writes the first one.</summary>
    /// <param name="domain">The domain the material was installed for.</param>
    /// <param name="site">The site it belongs to.</param>
    /// <param name="notAfter">When the installed certificate expires, as the agent parsed it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored certificate.</returns>
    /// <remarks>
    /// The lookup ignores the tenant filter because the unique index does: a row for this domain
    /// under a different account would otherwise be invisible here and then collide on insert. The
    /// row is only ever reached for a domain whose site the caller was already proved to own.
    /// </remarks>
    private async Task<Certificate> UpsertAsync(
        string domain,
        SiteSnapshot site,
        DateTimeOffset notAfter,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Certificates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(certificate => certificate.Domain == domain, cancellationToken);

        if (existing is not null)
        {
            _dbContext.Certificates.Remove(existing);
        }

        var certificate = new Certificate(
            Guid.NewGuid(),
            site.AccountId,
            site.Id,
            domain,
            CertificateSource.Custom,
            notAfter,
            _clock.UtcNow);

        _dbContext.Certificates.Add(certificate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return certificate;
    }

    /// <summary>Journals a refused installation and returns it as the typed failure.</summary>
    /// <param name="command">The installation that was refused, whose domain is the journal's subject.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<CertificateDto>> FailAsync(
        InstallCustomCertificateCommand command,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CertificateInstalled,
            command.Domain,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<CertificateDto>.Fail(Error.Of(code));
    }
}
