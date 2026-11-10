using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.CreateMigrationJob;

/// <summary>
/// Handles <see cref="CreateMigrationJobCommand"/>: copies what the discovery found for the chosen accounts into a
/// new draft job (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <b>The job holds a snapshot</b>, so running the discovery again cannot change a job in progress. The default
/// primary domain is the account's first site; the administrator maps the plan and the owner, which a source cannot
/// say. A source that is a Maran panel needs the pull origin and the fingerprint it was confirmed by; any other
/// source takes neither, because it is moved by rsync and not by a package.
/// </remarks>
public sealed class CreateMigrationJobCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The caller.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="currentUser">The caller.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public CreateMigrationJobCommandHandler(
        ServerMigrationDbContext dbContext,
        ICurrentUser currentUser,
        IClock clock,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
        _journal = journal;
        _names = names;
    }

    /// <summary>Creates the job.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job; <c>RemoteServerNotFound</c>, <c>RemoteServerNotReady</c>, <c>MigrationAccountNotFound</c> or <c>MigrationGrantMissing</c> otherwise.</returns>
    public async Task<Result<MigrationJobDto>> HandleAsync(
        CreateMigrationJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var server = await _dbContext.RemoteServers.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == command.ServerId, cancellationToken);
        if (server is null)
        {
            return Fail(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound);
        }

        if (!server.MayDiscover || _currentUser.Caller is not AdministratorCaller administrator)
        {
            return Fail(nameof(ErrorMessages.RemoteServerNotReady), ErrorType.Conflict);
        }

        var isMaran = server.Kind == RemoteSourceKind.Maran;
        if (isMaran != (command.SourceUrl is not null) || isMaran != (command.ExpectedKeyFingerprint is not null))
        {
            return Fail(nameof(ErrorMessages.MigrationGrantMissing), ErrorType.Conflict);
        }

        var discovered = await _dbContext.DiscoveredAccounts.AsNoTracking()
            .Include(row => row.Sites)
            .Include(row => row.Databases)
            .Where(row => row.RemoteServerId == server.Id && command.DiscoveredAccountIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        if (discovered.Count != command.DiscoveredAccountIds.Count)
        {
            return Fail(nameof(ErrorMessages.MigrationAccountNotFound), ErrorType.NotFound);
        }

        var now = _clock.UtcNow;
        var job = new MigrationJob(
            Guid.NewGuid(),
            server.Id,
            command.Name.Trim(),
            command.Concurrency ?? MigrationJobLimits.DefaultConcurrency,
            command.BandwidthLimitKib,
            command.SourceUrl,
            command.ExpectedKeyFingerprint,
            administrator.UserId,
            now);
        _dbContext.MigrationJobs.Add(job);

        foreach (var source in discovered.OrderBy(row =>
        {
            return row.Name;
        }, StringComparer.Ordinal))
        {
            var snapshot = new MigrationInventorySnapshot(
                source.Sites
                    .Select(site =>
                    {
                        return new MigrationSnapshotSite(
                        site.Domain,
                        site.AliasesText.Length == 0 ? [] : site.AliasesText.Split(' '),
                        site.Backend,
                        site.PhpVersion);
                    })
                    .ToList(),
                source.Databases
                    .Select(database =>
                    {
                        return new MigrationSnapshotDatabase(database.Name, database.SizeKnown ? database.SizeBytes : null);
                    })
                    .ToList());
            _dbContext.MigrationJobAccounts.Add(new MigrationJobAccount(
                Guid.NewGuid(),
                job.Id,
                source.Name,
                source.Home,
                source.DiskKnown ? source.DiskBytes : null,
                source.CronEntryCount,
                source.MailboxCount,
                MigrationSectionMapper.WriteInventory(snapshot),
                snapshot.Sites.Count > 0 ? snapshot.Sites[0].Domain : null));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.JobCreated, job.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<MigrationJobDto>.Ok(
            MigrationJobMapper.ToDto(job, await _dbContext.CountStatesAsync(job.Id, cancellationToken), _names));
    }

    /// <summary>Builds a failed result.</summary>
    /// <param name="code">The code.</param>
    /// <param name="type">The kind.</param>
    /// <returns>The failure.</returns>
    private static Result<MigrationJobDto> Fail(string code, ErrorType type)
    {
        return Result<MigrationJobDto>.Fail(Error.Of(code, type));
    }
}
