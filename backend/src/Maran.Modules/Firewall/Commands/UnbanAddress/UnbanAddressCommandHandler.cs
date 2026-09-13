using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Firewall.Commands.UnbanAddress;

/// <summary>
/// Handles <see cref="UnbanAddressCommand"/>: lifts every episode in force for one address and asks
/// the agent to stop dropping its packets.
/// </summary>
/// <remarks>
/// <para>
/// <b>An agent that reports no such ban is treated as success.</b> That is not leniency, it is the
/// one case this command exists to repair: a machine restarted before the reconciler ran holds no
/// ban in the kernel while these rows still say the address is banned, so the row is the only thing
/// keeping it out — and it would be re-applied by the next reconciliation pass. Refusing the unban
/// there would leave an address the administrator has just released permanently unreleasable through
/// the panel. Every other agent failure IS refused, and the rows stay as they were.
/// </para>
/// <para>
/// The episodes are marked lifted rather than deleted: the escalation ladder counts an address's
/// history, and an unban is part of that history rather than an erasure of it.
/// </para>
/// </remarks>
public sealed class UnbanAddressCommandHandler
{
    /// <summary>
    /// The agent client's code for "there was no active ban". Spelled out because the generated
    /// resource class it comes from is internal to <c>Maran.Agent.Client</c> and cannot be
    /// referenced from a module; pinned by a test so a rename over there does not turn this branch
    /// silently off.
    /// </summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The Firewall module's database context, which is the durable store of every ban.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The agent, which owns the host's ban set.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="agent">The agent client that lifts the ban.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    public UnbanAddressCommandHandler(
        FirewallDbContext dbContext,
        IAgentFirewallClient agent,
        IClock clock,
        FirewallAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Lifts the ban.</summary>
    /// <param name="command">Which address to let back in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>BanAddressInvalid</c>, <c>BanNotFound</c>, or the agent's typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(UnbanAddressCommand command, CancellationToken cancellationToken)
    {
        if (!IpAddressNormalizer.TryNormalize(command.Address, out var address))
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressUnbanned,
                command.Address,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.BanAddressInvalid), ErrorType.Validation));
        }

        var subject = address.ToString();
        var now = _clock.UtcNow;

        var episodes = await _dbContext.BanEpisodes
            .Where(episode => episode.IpAddress == subject && episode.LiftedAt == null)
            .ToListAsync(cancellationToken);

        var inForce = episodes.Where(episode =>
        {
            return episode.IsInForce(now);
        }).ToList();

        if (inForce.Count == 0)
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressUnbanned, subject, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.BanNotFound), ErrorType.NotFound));
        }

        var lifted = await _agent.UnbanAsync(subject, cancellationToken);
        if (!lifted.IsSuccess && !string.Equals(lifted.Error!.Code, AgentNotFoundCode, StringComparison.Ordinal))
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressUnbanned, subject, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<bool>.Fail(lifted.Error!);
        }

        foreach (var episode in inForce)
        {
            episode.Lift(now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AddressUnbanned, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
