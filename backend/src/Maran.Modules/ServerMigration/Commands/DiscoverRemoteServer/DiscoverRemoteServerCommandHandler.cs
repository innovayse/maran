using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.DiscoverRemoteServer;

/// <summary>Reads a registered server's inventory through the agent and replaces what the panel stored of it.</summary>
/// <remarks>
/// <para>
/// <b>The stream is collected whole, bounded, and stored only if it finished.</b> The agent states every ending
/// itself; an inventory that failed half way, or that ran past what the panel will hold
/// (<see cref="DiscoveryLimits"/>), is refused and leaves the previous discovery exactly as it was, because a list of
/// "the first 3000 accounts" shown as the server's accounts would be a statement the panel cannot stand behind. A
/// stream that stops without an ending is a failure too, never a completion.
/// </para>
/// <para>
/// <b>The replacement is one transaction.</b> The old accounts, sites, databases and warnings are removed and the
/// new ones added before a single save, so a reader sees the old discovery or the new one and never a mixture. The
/// cancellation of the request abandons the stream and stores nothing.
/// </para>
/// <para>
/// A source that names the same login twice is stored once, the first time: a login is unique on a real system, so
/// a repeat is the source's confusion and not a second account.
/// </para>
/// <para>
/// Nothing is written to the source, and the private key appears in no entry of the journal (the entry names the
/// server's address and identifier).
/// </para>
/// </remarks>
public sealed class DiscoverRemoteServerCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The agent's connection to remote servers.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>The clock the discovery is dated by.</summary>
    private readonly IClock _clock;

    /// <summary>The module's display names, which word the warnings in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="agent">The agent's connection to remote servers.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock the discovery is dated by.</param>
    /// <param name="names">The module's display names.</param>
    public DiscoverRemoteServerCommandHandler(
        ServerMigrationDbContext dbContext,
        IAgentTransferClient agent,
        ServerMigrationAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _clock = clock;
        _names = names;
    }

    /// <summary>Runs the discovery and stores its result.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// When it finished, how many accounts it found and its warnings; <c>RemoteServerNotFound</c> for an identifier
    /// that names none; <c>RemoteServerNotReady</c> when no host key is pinned or the last attempt was refused; or
    /// the translated reason the inventory could not be read (<see cref="MigrationAgentErrorTranslator"/>).
    /// </returns>
    public async Task<Result<DiscoveryResultDto>> HandleAsync(
        DiscoverRemoteServerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var server = await _dbContext.RemoteServers.FirstOrDefaultAsync(
            row => row.Id == command.ServerId, cancellationToken);
        if (server is null)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.OfMissing(command.ServerId),
                Error.Of(nameof(ErrorMessages.RemoteServerNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        if (!server.MayDiscover)
        {
            return await RefuseAsync(
                command,
                RemoteServerAuditSubject.Of(server),
                Error.Of(nameof(ErrorMessages.RemoteServerNotReady), ErrorType.Conflict),
                cancellationToken);
        }

        var collection = await CollectAsync(server, cancellationToken);
        if (collection is not CollectedInventory collected)
        {
            // The hierarchy is closed to this assembly: an inventory either finished or failed.
            return await RefuseDiscoveryAsync(
                command, server, (FailedInventory)collection, cancellationToken);
        }

        await _dbContext.DiscardDiscoveryAsync(server.Id, cancellationToken);

        var storedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in collected.Accounts)
        {
            if (storedNames.Add(source.Name))
            {
                _dbContext.DiscoveredAccounts.Add(DiscoveredInventoryMapper.ToAccount(server.Id, source));
            }
        }

        var warnings = collected.Warnings
            .Select(source =>
            {
                return DiscoveredInventoryMapper.ToWarning(server.Id, source);
            })
            .ToList();
        _dbContext.DiscoveryWarnings.AddRange(warnings);

        var recorded = server.RecordDiscovery(storedNames.Count, _clock.UtcNow);
        if (!recorded.IsSuccess)
        {
            return await RefuseAsync(command, RemoteServerAuditSubject.Of(server), recorded.Error!, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerDiscovered,
            RemoteServerAuditSubject.Of(server),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DiscoveryResultDto>.Ok(new DiscoveryResultDto(
            server.DiscoveredAt!.Value,
            storedNames.Count,
            warnings
                .Select(warning =>
                {
                    return DiscoveryWarningMapper.From(warning, _names);
                })
                .ToList()));
    }

    /// <summary>Reads the agent's stream to its ending, within the bounds the panel will hold.</summary>
    /// <param name="server">The server to read.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>What the stream reported, or the failure it ended with.</returns>
    private async Task<InventoryOutcome> CollectAsync(RemoteServer server, CancellationToken cancellationToken)
    {
        var accounts = new List<AgentSourceAccount>();
        var warnings = new List<AgentSourceWarning>();

        var stream = _agent.ReadSourceInventoryAsync(
            RemoteServerAgentMapper.EndpointOf(server),
            RemoteServerAgentMapper.CredentialOf(server),
            cancellationToken);

        await foreach (var inventoryEvent in stream.WithCancellation(cancellationToken))
        {
            switch (inventoryEvent)
            {
                case AgentSourceInventoryAccountFound found:
                    if (accounts.Count >= DiscoveryLimits.MaxAccounts)
                    {
                        return Overrun();
                    }

                    accounts.Add(found.Account);
                    break;

                case AgentSourceInventoryWarningRaised raised:
                    if (warnings.Count >= DiscoveryLimits.MaxWarnings)
                    {
                        return Overrun();
                    }

                    warnings.Add(raised.Warning);
                    break;

                case AgentSourceInventoryFailed failed:
                    return new FailedInventory(failed.Error, failed.Reason);

                case AgentSourceInventoryDone:
                    return new CollectedInventory(accounts, warnings);
            }
        }

        return new FailedInventory(
            Error.Of(nameof(ErrorMessages.MigrationDiscoveryFailed), ErrorType.Failure),
            SourceFailureReason.Unspecified);
    }

    /// <summary>The failure for a source that reported more than the panel will hold.</summary>
    /// <returns>A failed collection; nothing of the stream is kept.</returns>
    private static FailedInventory Overrun()
    {
        return new FailedInventory(
            Error.Of(nameof(ErrorMessages.MigrationDiscoveryFailed), ErrorType.Failure),
            SourceFailureReason.Unspecified);
    }

    /// <summary>
    /// Answers a discovery that failed: records on the server a refusal that describes it, journals the attempt, and
    /// translates the reason.
    /// </summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="server">The server that was read.</param>
    /// <param name="failed">How the stream ended.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<DiscoveryResultDto>> RefuseDiscoveryAsync(
        DiscoverRemoteServerCommand command,
        RemoteServer server,
        FailedInventory failed,
        CancellationToken cancellationToken)
    {
        if (MigrationAgentErrorTranslator.RecordedFailureOf(failed.Reason) is { } failure)
        {
            // The refusal describes the server (its key changed, it refused the login, it did not answer), so the
            // row says so and the next attempt is a decision. A failure with no reason says nothing about it.
            server.RecordTestFailed(failure, _clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await RefuseAsync(
            command,
            RemoteServerAuditSubject.Of(server),
            MigrationAgentErrorTranslator.Translate(failed.Error, failed.Reason),
            cancellationToken);
    }

    /// <summary>Journals a refused discovery and answers with the reason.</summary>
    /// <param name="command">The request, for the caller's address and agent.</param>
    /// <param name="subject">What the discovery was attempted on.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<DiscoveryResultDto>> RefuseAsync(
        DiscoverRemoteServerCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerDiscovered,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DiscoveryResultDto>.Fail(error);
    }
}
