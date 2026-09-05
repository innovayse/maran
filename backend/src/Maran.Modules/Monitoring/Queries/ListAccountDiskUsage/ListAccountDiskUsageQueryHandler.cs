using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>
/// Handles <see cref="ListAccountDiskUsageQuery"/> by joining what the agent measured on the
/// filesystem to what the panel knows each account is allowed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The join is on the system user name, because that is the only identifier both sides carry.</b>
/// The agent has never heard of an account id — it reads the host's password database and walks home
/// directories — and the panel's snapshot carries the same name because every agent operation on a
/// customer's files is addressed by it. Ordinal comparison, since a Linux user name is
/// case-sensitive: <c>Alice</c> and <c>alice</c> are two users, and a culture-aware match would
/// silently bill one for the other's bytes.
/// </para>
/// <para>
/// <b>The listing is the PANEL's accounts, not the agent's rows.</b> Iterating the panel's side is
/// what drops a row the agent reported and the panel does not know: on a real host that is a system
/// account whose name happens to parse as an account name, and billing it to nobody as if it were a
/// customer would be inventing a tenant. The agent already declines to report obvious service users
/// and checks each row's home against the one it would have created — this is the second half of the
/// same question, asked from the side that actually owns the answer.
/// </para>
/// <para>
/// <b>An account the agent did not measure has no figure, and a missing figure is <c>null</c>.</b>
/// Never 0 — the module's standing rule that a zero is a claim, not an absence. The account may have
/// been created seconds ago, or its home may be unreadable; either way nobody has measured it, and a
/// row reading "0 of 1 GB" would be a statement about a customer that is not true.
/// </para>
/// </remarks>
public sealed class ListAccountDiskUsageQueryHandler
{
    /// <summary>How many bytes one megabyte is, for converting a plan's allowance.</summary>
    private const long BytesPerMegabyte = 1024L * 1024L;

    /// <summary>The agent, the only thing in the system that can measure a home directory.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>The window onto the Accounts module, which owns the accounts and their plans.</summary>
    /// <remarks>
    /// Its <see cref="IAccountDirectory.ListAsync"/> applies NO tenant scope and hands back every
    /// account on the host — its own remarks say so at length. That is safe here and only here
    /// because every route of this module's controller is gated to administrators, which an
    /// integration test asserts on each of them in both directions.
    /// </remarks>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that measures each account's home directory.</param>
    /// <param name="accounts">The directory that knows the accounts and their plans' allowances.</param>
    public ListAccountDiskUsageQueryHandler(IAgentMonitorClient agent, IAccountDirectory accounts)
    {
        _agent = agent;
        _accounts = accounts;
    }

    /// <summary>Returns one row per hosting account the panel knows, ordered by user name.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The listing, or the agent's own typed failure.</returns>
    /// <remarks>
    /// The agent is asked first so that its failure short-circuits before the panel's own tables are
    /// read: without a measurement there is no listing to build, only a column of allowances, and
    /// answering with that would look like a set of accounts using nothing at all.
    /// </remarks>
    public async Task<Result<IReadOnlyList<AccountDiskUsageDto>>> HandleAsync(
        ListAccountDiskUsageQuery query,
        CancellationToken cancellationToken)
    {
        var measured = await _agent.GetAccountsDiskUsageAsync(cancellationToken);

        if (!measured.IsSuccess)
        {
            return Result<IReadOnlyList<AccountDiskUsageDto>>.Fail(measured.Error!);
        }

        var usedByUsername = ByUsername(measured.Value);
        var accounts = await _accounts.ListAsync(cancellationToken);

        var rows = accounts
            .Select(account =>
            {
                return ToRow(account, usedByUsername);
            })
            .OrderBy(row =>
            {
                return row.Username;
            }, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<AccountDiskUsageDto>>.Ok(rows);
    }

    /// <summary>Indexes the agent's measurements by the user name they were taken under.</summary>
    /// <param name="measured">What the agent reported.</param>
    /// <returns>Used bytes per user name.</returns>
    /// <remarks>
    /// Built by assignment rather than by <c>ToDictionary</c>, which throws on a repeated key. The
    /// agent deduplicates its own listing today, but it is a separate process on the other side of a
    /// version boundary, and a panel that crashed on a surprising response would take the whole
    /// dashboard down to report a duplicated passwd row. The last reading of a repeated name wins,
    /// which for two identical rows is the same answer either way.
    /// </remarks>
    private static Dictionary<string, ulong> ByUsername(IReadOnlyList<AgentAccountDiskUsage> measured)
    {
        var usedByUsername = new Dictionary<string, ulong>(measured.Count, StringComparer.Ordinal);

        foreach (var account in measured)
        {
            usedByUsername[account.AccountUsername] = account.UsedBytes;
        }

        return usedByUsername;
    }

    /// <summary>Builds one account's row from its snapshot and whatever the agent measured for it.</summary>
    /// <param name="account">The account, as the Accounts module described it.</param>
    /// <param name="usedByUsername">The agent's measurements, indexed by user name.</param>
    /// <returns>The row.</returns>
    private static AccountDiskUsageDto ToRow(AccountSnapshot account, Dictionary<string, ulong> usedByUsername)
    {
        // NULL, not zero, and the nullable type is the whole reason this method exists. An account
        // the agent did not measure — because the agent was down, or because a rename left the
        // panel's user name and the host's disagreeing in case — has NO figure. Reporting 0 says
        // the opposite of "unknown": it tells an operator, confidently, that the account is using
        // nothing, which is exactly the reading that hides a full disk during an agent outage.
        long? usedBytes = usedByUsername.TryGetValue(account.Username, out var used)
            ? ToSignedBytes(used)
            : null;

        return new AccountDiskUsageDto(account.Id, account.Username, usedBytes, ToQuotaBytes(account.DiskQuotaMb));
    }

    /// <summary>Converts a plan's allowance from the megabytes it is stored in to bytes.</summary>
    /// <param name="diskQuotaMb">The allowance, in megabytes.</param>
    /// <returns>The same allowance in bytes.</returns>
    /// <remarks>
    /// The multiplication is done in 64 bits, which is the point of this method existing at all: an
    /// <c>int</c> megabyte count times an <c>int</c> 1,048,576 overflows above about 2,047 MB, so the
    /// obvious arithmetic turns a 4 GB plan into a NEGATIVE allowance — and every account on it into
    /// one that appears to be over quota.
    /// </remarks>
    private static long ToQuotaBytes(int diskQuotaMb)
    {
        return diskQuotaMb * BytesPerMegabyte;
    }

    /// <summary>Narrows the agent's unsigned byte count onto the signed one the panel carries.</summary>
    /// <param name="value">The figure the agent reported.</param>
    /// <returns>The same value, saturated at <see cref="long.MaxValue"/>.</returns>
    /// <remarks>
    /// The same saturation <c>MetricsSampler</c> applies for the same reason: an unchecked cast turns
    /// a value above <see cref="long.MaxValue"/> into a NEGATIVE byte count, which would draw as an
    /// account using less than nothing. The ceiling is over nine exabytes, so this is about what the
    /// type system permits rather than about what a home directory will hold.
    /// </remarks>
    private static long ToSignedBytes(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}
