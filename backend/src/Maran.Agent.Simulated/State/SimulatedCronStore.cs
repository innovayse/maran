using System.Security.Cryptography;
using System.Text;
using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// Every account's crontab on the simulated server, bounded and safe to share between requests.
/// </summary>
/// <remarks>
/// <para>
/// <b>A command is data.</b> It is stored, listed and compared, and nothing here interprets it: a
/// run by hand returns a fabricated result derived from the command's text and never starts a
/// program, because there is no host to start one on.
/// </para>
/// <para>
/// Entry ids are uuid-shaped like the real agent's, derived from the account and a per-crontab
/// sequence instead of a random source, so the same actions in the same order produce the same ids.
/// </para>
/// </remarks>
public sealed class SimulatedCronStore
{
    /// <summary>The shortest run the fabricated result reports, in milliseconds.</summary>
    private const long ShortestRunMilliseconds = 50;

    /// <summary>How many milliseconds above the shortest the fabricated duration may reach.</summary>
    private const long RunSpreadMilliseconds = 1_450;

    /// <summary>Serialises every read and write of the crontabs.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The crontabs, keyed by account.</summary>
    private readonly Dictionary<string, SimulatedCronTable> _tables = [];

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedCronStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>Lists the entries an account's crontab holds.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <returns>The entries in installation order; empty for an account with none.</returns>
    public IReadOnlyList<AgentCronEntry> List(string account)
    {
        lock (_gate)
        {
            return _tables.TryGetValue(account, out var table) ? table.Entries.ToList() : [];
        }
    }

    /// <summary>Appends an entry, within a stated allowance.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="schedule">When the entry is to run.</param>
    /// <param name="command">The command line, stored verbatim.</param>
    /// <param name="maxEntries">The plan's allowance for the account, or <c>null</c> for none.</param>
    /// <returns>
    /// The new entry's id, <c>AgentAlreadyExists</c> for an entry with the same schedule and command,
    /// <c>AgentLimitReached</c> when the allowance or a built-in ceiling is met, or
    /// <c>AgentInvalidInput</c> for a blank or over-long schedule field or command.
    /// </returns>
    public Result<string> Create(string account, AgentCronSchedule schedule, string command, uint? maxEntries)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength) || !IsUsable(schedule, command))
        {
            return Result<string>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            var table = TableFor(account);
            if (table.Entries.Any(entry => { return entry.Schedule == schedule && entry.Command == command; }))
            {
                return Result<string>.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if ((maxEntries is not null && table.Entries.Count >= maxEntries)
                || table.Entries.Count >= _limits.MaxCronEntriesPerAccount
                || _tables.Values.Sum(other => { return other.Entries.Count; }) >= _limits.MaxCronEntriesTotal)
            {
                return Result<string>.Fail(SimulatedAgentErrors.LimitReached());
            }

            var entryId = MintId(account, table.MintedIds++);
            table.Entries.Add(new AgentCronEntry(entryId, schedule, command, true));

            return Result<string>.Ok(entryId);
        }
    }

    /// <summary>Replaces an entry's schedule and command, leaving its enablement as it was.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to rewrite.</param>
    /// <param name="schedule">The new schedule.</param>
    /// <param name="command">The new command line.</param>
    /// <returns>Success, <c>AgentNotFound</c> for an entry that is not there, or <c>AgentInvalidInput</c>.</returns>
    public Result Update(string account, string entryId, AgentCronSchedule schedule, string command)
    {
        if (!IsUsable(schedule, command))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            return Replace(account, entryId, entry =>
            {
                return entry with { Schedule = schedule, Command = command };
            });
        }
    }

    /// <summary>Enables or disables an entry without touching its schedule or command.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to switch.</param>
    /// <param name="enabled">Whether the entry is live.</param>
    /// <returns>Success, or <c>AgentNotFound</c> for an entry that is not there.</returns>
    public Result SetEnabled(string account, string entryId, bool enabled)
    {
        lock (_gate)
        {
            return Replace(account, entryId, entry => { return entry with { Enabled = enabled }; });
        }
    }

    /// <summary>Removes an entry and what its last run left.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to remove.</param>
    /// <returns>Success, or <c>AgentNotFound</c> for an entry that is not there.</returns>
    public Result Delete(string account, string entryId)
    {
        lock (_gate)
        {
            if (!_tables.TryGetValue(account, out var table)
                || table.Entries.RemoveAll(entry => { return entry.EntryId == entryId; }) == 0)
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            table.LastRuns.Remove(entryId);

            return Result.Ok();
        }
    }

    /// <summary>
    /// Suppresses or restores every managed entry of an account, orthogonally to each entry's own enablement.
    /// </summary>
    /// <param name="account">System username of the account.</param>
    /// <param name="suspended">Whether every managed entry is suppressed.</param>
    /// <remarks>
    /// An account with no crontab has nothing to suppress, so the call changes nothing and still succeeds.
    /// </remarks>
    public void SetSuspended(string account, bool suspended)
    {
        lock (_gate)
        {
            if (_tables.TryGetValue(account, out var table))
            {
                table.Suspended = suspended;
            }
        }
    }

    /// <summary>Reads what an entry's most recent run left behind.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to read.</param>
    /// <returns>
    /// The last run, <c>null</c> when the entry never ran, or <c>AgentNotFound</c> for an entry that is not there.
    /// </returns>
    public Result<AgentCronRunOutput?> GetOutput(string account, string entryId)
    {
        lock (_gate)
        {
            if (!HasEntry(account, entryId))
            {
                return Result<AgentCronRunOutput?>.Fail(SimulatedAgentErrors.NotFound());
            }

            return Result<AgentCronRunOutput?>.Ok(_tables[account].LastRuns.GetValueOrDefault(entryId));
        }
    }

    /// <summary>Records what an entry's last run left behind; how a seed gives a screen a history.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry.</param>
    /// <param name="output">The last run's output, exit code and time.</param>
    /// <returns>Success, or <c>AgentNotFound</c> for an entry that is not there.</returns>
    public Result RecordRun(string account, string entryId, AgentCronRunOutput output)
    {
        lock (_gate)
        {
            if (!HasEntry(account, entryId))
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            _tables[account].LastRuns[entryId] = output;

            return Result.Ok();
        }
    }

    /// <summary>Runs an entry "now": fabricates a clean exit and records it as the entry's last run.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to run.</param>
    /// <param name="now">The instant the run is recorded at.</param>
    /// <returns>
    /// The run — exit code zero, no output, a duration derived from the command's text — or
    /// <c>AgentNotFound</c> for an entry that is not there, or <c>AgentAccountSuspended</c> for a
    /// suspended account, in which case nothing is recorded.
    /// </returns>
    public Result<AgentCronRun> Run(string account, string entryId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!HasEntry(account, entryId))
            {
                return Result<AgentCronRun>.Fail(SimulatedAgentErrors.NotFound());
            }

            var table = _tables[account];
            if (table.Suspended)
            {
                return Result<AgentCronRun>.Fail(SimulatedAgentErrors.AccountSuspended());
            }

            var command = table.Entries.First(entry => { return entry.EntryId == entryId; }).Command;
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(command));
            var duration = ShortestRunMilliseconds + (BitConverter.ToUInt16(digest) % RunSpreadMilliseconds);
            table.LastRuns[entryId] = new AgentCronRunOutput(string.Empty, 0, now.ToUnixTimeSeconds());

            return Result<AgentCronRun>.Ok(
                new AgentCronRun(duration, new AgentCronRunExited(0), string.Empty, string.Empty));
        }
    }

    /// <summary>Reads the managed environment assignments of an account's crontab.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <returns>The assignments in order; empty for an account with none.</returns>
    public IReadOnlyList<AgentCronEnvVar> GetEnvironment(string account)
    {
        lock (_gate)
        {
            return _tables.TryGetValue(account, out var table) ? table.Environment.ToList() : [];
        }
    }

    /// <summary>Replaces the managed environment assignments whole.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="variables">The complete new set; empty clears every managed assignment.</param>
    /// <returns>
    /// Success, or <c>AgentInvalidInput</c> for too many, a blank or over-long name, or an over-long value.
    /// </returns>
    public Result SetEnvironment(string account, IReadOnlyList<AgentCronEnvVar> variables)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || variables.Count > _limits.MaxCronEnvironmentVariables
            || variables.Any(variable =>
            {
                return string.IsNullOrWhiteSpace(variable.Name)
                    || variable.Name.Length > _limits.MaxCronEnvironmentNameLength
                    || variable.Value.Length > _limits.MaxCronEnvironmentValueLength;
            }))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            var table = TableFor(account);
            table.Environment.Clear();
            table.Environment.AddRange(variables);

            return Result.Ok();
        }
    }

    /// <summary>Whether a schedule and command are present and short enough to store.</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="command">The command line.</param>
    /// <returns><c>true</c> when every field is non-blank and within its ceiling.</returns>
    private bool IsUsable(AgentCronSchedule schedule, string command)
    {
        return SimulatedInput.IsBoundedName(schedule.Minute, _limits.MaxNameLength)
            && SimulatedInput.IsBoundedName(schedule.Hour, _limits.MaxNameLength)
            && SimulatedInput.IsBoundedName(schedule.DayOfMonth, _limits.MaxNameLength)
            && SimulatedInput.IsBoundedName(schedule.Month, _limits.MaxNameLength)
            && SimulatedInput.IsBoundedName(schedule.DayOfWeek, _limits.MaxNameLength)
            && !string.IsNullOrWhiteSpace(command)
            && command.Length <= _limits.MaxCronCommandLength;
    }

    /// <summary>Derives a uuid-shaped entry id from the account and the crontab's sequence number.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="sequence">How many ids the crontab had minted before this one.</param>
    /// <returns>A lowercase hyphenated uuid.</returns>
    private static string MintId(string account, int sequence)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{account}:{sequence}"));

        return new Guid(digest.AsSpan(0, 16)).ToString("D");
    }

    /// <summary>Whether an account's crontab holds an entry; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry.</param>
    /// <returns><c>true</c> when the entry exists.</returns>
    private bool HasEntry(string account, string entryId)
    {
        return _tables.TryGetValue(account, out var table) && table.Entries.Any(entry =>
        {
            return entry.EntryId == entryId;
        });
    }

    /// <summary>Finds or creates an account's crontab; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <returns>The crontab.</returns>
    private SimulatedCronTable TableFor(string account)
    {
        if (!_tables.TryGetValue(account, out var table))
        {
            table = new SimulatedCronTable();
            _tables[account] = table;
        }

        return table;
    }

    /// <summary>Rewrites one entry in place; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry.</param>
    /// <param name="rewrite">How the entry changes.</param>
    /// <returns>Success, or <c>AgentNotFound</c> for an entry that is not there.</returns>
    private Result Replace(string account, string entryId, Func<AgentCronEntry, AgentCronEntry> rewrite)
    {
        if (!_tables.TryGetValue(account, out var table))
        {
            return Result.Fail(SimulatedAgentErrors.NotFound());
        }

        var index = table.Entries.FindIndex(entry => { return entry.EntryId == entryId; });
        if (index < 0)
        {
            return Result.Fail(SimulatedAgentErrors.NotFound());
        }

        table.Entries[index] = rewrite(table.Entries[index]);

        return Result.Ok();
    }
}
