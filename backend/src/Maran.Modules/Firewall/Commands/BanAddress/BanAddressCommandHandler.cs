using System.Net;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Firewall.Commands.BanAddress;

/// <summary>
/// Handles <see cref="BanAddressCommand"/>: normalises the address, asks the agent to drop it, and
/// records the episode that will survive the next reboot.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent first, the row second.</b> A ban installed with no row is visible in the kernel,
/// retryable, and lost at the next restart — bad, but recoverable and obvious. A row written for a
/// ban the agent refused is worse: the panel would report an address as banned while every packet
/// from it still arrives, and the reconciler would go on re-applying a ban that has never once
/// existed.
/// </para>
/// <para>
/// <b>The address is normalised before anything else happens to it.</b> Everything downstream — the
/// agent call, the row, the journal entry — uses the normalised form, so the panel never holds two
/// spellings of one address.
/// </para>
/// <para>
/// <b>A loopback address is refused here as well as at the agent, and the refusal names itself.</b>
/// The agent's <c>BanAddress::parse</c> is the gate that must not be removed — it is the last one
/// every caller passes, and it survives the panel's own data being wrong — but everything it can
/// tell a caller arrives as <c>AgentInvalidInput</c>, one wire code that also covers a certificate
/// whose key does not match its chain. One sentence standing in for two unrelated remedies tells
/// neither reader what to change, and a refusal an operator cannot act on is a refusal an operator
/// routes around. Refusing at the boundary too is the ordinary arrangement of rules/security.md
/// item 1 — validate at the boundary, revalidate in the agent — and it is what lets this module own
/// the sentence, in all three locales, saying what to ban instead.
/// </para>
/// <para>
/// <b>A ban on an address already banned MOVES the standing episode; it does not add a second.</b>
/// The host's ban set is keyed by address, so the agent's second <c>add element</c> replaces the
/// timeout and one element remains. Two rows would then disagree about that one element, and the
/// ban list is the only evidence of a ban that exists anywhere. Refusing the re-ban with a conflict,
/// the way a colliding rule is refused, was the other candidate and is rejected: a rule asked for
/// twice is the same rule, while a ban asked for again with a different duration is a NEW
/// instruction about the host, and the only way to carry it out under a refusal would be to unban
/// first — a hole in the ban, opened by hand, in the middle of the attack it was placed against.
/// </para>
/// </remarks>
public sealed class BanAddressCommandHandler
{
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
    /// <param name="agent">The agent client that installs the ban.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    public BanAddressCommandHandler(
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

    /// <summary>Bans the address.</summary>
    /// <param name="command">Which address to ban, and for how long.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>BanAddressInvalid</c>, or the agent's own typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(BanAddressCommand command, CancellationToken cancellationToken)
    {
        if (!IpAddressNormalizer.TryNormalize(command.Address, out var address))
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressBanned,
                command.Address,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.BanAddressInvalid), ErrorType.Validation));
        }

        if (IPAddress.IsLoopback(address))
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressBanned,
                address.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.BanAddressLoopback), ErrorType.Validation));
        }

        var subject = address.ToString();
        var ttl = command.DurationMinutes is null
            ? (TimeSpan?)null
            : TimeSpan.FromMinutes(command.DurationMinutes.Value);

        var banned = await _agent.BanAsync(subject, ttl, cancellationToken);
        if (!banned.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                AuditActions.AddressBanned, subject, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<bool>.Fail(banned.Error!);
        }

        var now = _clock.UtcNow;
        var expiresAt = ttl is null ? (DateTimeOffset?)null : now + ttl.Value;

        var standing = await _dbContext.BanEpisodes
            .Where(episode => episode.IpAddress == subject && episode.LiftedAt == null)
            .ToListAsync(cancellationToken);

        var inForce = standing.Where(episode =>
        {
            return episode.IsInForce(now);
        }).ToList();

        if (inForce.Count == 0)
        {
            _dbContext.BanEpisodes.Add(new BanEpisode(
                Guid.NewGuid(),
                subject,
                BanReason.Manual,
                windowStart: null,
                failures: 0,
                now,
                expiresAt));
        }
        else
        {
            // Every episode in force, not merely the newest: a host that has already collected a
            // contradictory pair — this handler used to write one on every re-ban — is repaired by
            // the next ban rather than left carrying it until somebody lifts the address.
            foreach (var episode in inForce)
            {
                episode.Reschedule(expiresAt);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AddressBanned, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
