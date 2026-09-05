using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.CronService;

/// <summary>Maps the agent's cron rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which can name
/// paths under the account's home — is logged rather than returned (rules/security.md item 8).
///
/// Three fields of the cron contract are deprecated and written as zeros by the agent
/// (<c>CronEntry.last_exit_code</c>, <c>CronEntry.last_run_at_unix</c> and
/// <c>UpdateCronEntryRequest.enabled</c>). None of them is read or written here, and the panel-side
/// types have no member for them: a zero copied out of a field the agent never fills is a claim
/// nobody made, and it reads exactly like a measurement.
/// </remarks>
public sealed class AgentCronClient : IAgentCronClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ICronServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentCronClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentCronClient(ICronServiceInvoker invoker, ILogger<AgentCronClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentCronClient(GrpcChannel channel, ILogger<AgentCronClient> logger)
        : this(new GrpcCronServiceInvoker(new V1.CronService.CronServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new ListCronEntriesRequest { AccountUsername = accountUsername };
        var response = await _invoker.ListCronEntriesAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListCronEntriesResponse.ResultOneofCase.Ok => ToEntriesResult(response.Ok),
            ListCronEntriesResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentCronEntry>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListEntriesAsync))),
            _ => Result<IReadOnlyList<AgentCronEntry>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        var request = new CreateCronEntryRequest
        {
            AccountUsername = accountUsername,
            Schedule = ToWireSchedule(schedule),
            Command = command,
        };
        var response = await _invoker.CreateCronEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateCronEntryResponse.ResultOneofCase.Ok => Result<string>.Ok(response.Ok.EntryId),
            CreateCronEntryResponse.ResultOneofCase.Error => Result<string>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateEntryAsync))),
            _ => Result<string>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        var request = new UpdateCronEntryRequest
        {
            AccountUsername = accountUsername,
            EntryId = entryId,
            Schedule = ToWireSchedule(schedule),
            Command = command,

            // Enabled is deliberately not set. The agent never reads it, and proto3 would send the
            // default anyway; leaving it out here is what keeps the two operations separate in this
            // file as well as on the wire. SetEntryEnabledAsync is the only way enablement changes.
        };
        var response = await _invoker.UpdateCronEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            UpdateCronEntryResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            UpdateCronEntryResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(UpdateEntryAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        var request = new DeleteCronEntryRequest
        {
            AccountUsername = accountUsername,
            EntryId = entryId,
        };
        var response = await _invoker.DeleteCronEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteCronEntryResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteCronEntryResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteEntryAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var request = new SetCronEntryEnabledRequest
        {
            AccountUsername = accountUsername,
            EntryId = entryId,
            Enabled = enabled,
        };
        var response = await _invoker.SetCronEntryEnabledAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetCronEntryEnabledResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetCronEntryEnabledResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetEntryEnabledAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        var request = new GetCronEntryOutputRequest
        {
            AccountUsername = accountUsername,
            EntryId = entryId,
        };
        var response = await _invoker.GetCronEntryOutputAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            GetCronEntryOutputResponse.ResultOneofCase.Ok => Result<AgentCronRunOutput?>.Ok(
                ToRunOutput(response.Ok)),
            GetCronEntryOutputResponse.ResultOneofCase.Error => Result<AgentCronRunOutput?>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetEntryOutputAsync))),
            _ => Result<AgentCronRunOutput?>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new GetCronEnvironmentRequest { AccountUsername = accountUsername };
        var response = await _invoker.GetCronEnvironmentAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            GetCronEnvironmentResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentCronEnvVar>>.Ok(
                ToVariables(response.Ok)),
            GetCronEnvironmentResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentCronEnvVar>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetEnvironmentAsync))),
            _ => Result<IReadOnlyList<AgentCronEnvVar>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        var request = new SetCronEnvironmentRequest { AccountUsername = accountUsername };
        foreach (var variable in variables)
        {
            request.Variables.Add(new CronEnvironmentVariable
            {
                Name = variable.Name,
                Value = variable.Value,
            });
        }

        var response = await _invoker.SetCronEnvironmentAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetCronEnvironmentResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetCronEnvironmentResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetEnvironmentAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Maps the panel's schedule onto its wire counterpart.</summary>
    /// <param name="schedule">The five fields the caller supplied.</param>
    /// <returns>The wire message carrying the same five fields, unaltered.</returns>
    private static CronSchedule ToWireSchedule(AgentCronSchedule schedule)
    {
        return new CronSchedule
        {
            Minute = schedule.Minute,
            Hour = schedule.Hour,
            DayOfMonth = schedule.DayOfMonth,
            Month = schedule.Month,
            DayOfWeek = schedule.DayOfWeek,
        };
    }

    /// <summary>Projects the wire listing onto the panel's DTOs, or refuses a row without a schedule.</summary>
    /// <param name="ok">The success payload of <c>ListCronEntries</c>.</param>
    /// <returns>
    /// The entries in the order the agent sent them, or <c>AgentInvalidResponse</c> when a row
    /// carries no schedule at all.
    /// </returns>
    /// <remarks>
    /// A schedule is a nested message, so proto3 lets it be absent, and an absent one has no honest
    /// projection: five empty fields would render as an entry that runs at no time, and inventing
    /// <c>* * * * *</c> would render as one that runs every minute. Neither is what the agent said,
    /// so the whole listing is refused rather than one row being quietly repaired — a listing that
    /// dropped the row instead would show a customer a crontab shorter than the one installed.
    /// </remarks>
    private static Result<IReadOnlyList<AgentCronEntry>> ToEntriesResult(ListCronEntriesOk ok)
    {
        var entries = new List<AgentCronEntry>(ok.Entries.Count);

        foreach (var entry in ok.Entries)
        {
            if (entry.Schedule is null)
            {
                return Result<IReadOnlyList<AgentCronEntry>>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            entries.Add(new AgentCronEntry(
                entry.EntryId,
                new AgentCronSchedule(
                    entry.Schedule.Minute,
                    entry.Schedule.Hour,
                    entry.Schedule.DayOfMonth,
                    entry.Schedule.Month,
                    entry.Schedule.DayOfWeek),
                entry.Command,
                entry.Enabled));
        }

        return Result<IReadOnlyList<AgentCronEntry>>.Ok(entries);
    }

    /// <summary>Projects the wire environment listing onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>GetCronEnvironment</c>.</param>
    /// <returns>The assignments in the order the crontab holds them.</returns>
    private static List<AgentCronEnvVar> ToVariables(GetCronEnvironmentOk ok)
    {
        var variables = new List<AgentCronEnvVar>(ok.Variables.Count);

        foreach (var variable in ok.Variables)
        {
            variables.Add(new AgentCronEnvVar(variable.Name, variable.Value));
        }

        return variables;
    }

    /// <summary>Projects what the last run left behind, or nothing at all when no run is evidenced.</summary>
    /// <param name="ok">The success payload of <c>GetCronEntryOutput</c>.</param>
    /// <returns>
    /// Null when the agent set none of the three fields, and otherwise a value keeping each unset
    /// field as null rather than as its proto3 default.
    /// </returns>
    /// <remarks>
    /// The three defaults are all meaningful values, which is why none of them may stand in for
    /// absence: an empty string is a run that printed nothing, a 0 exit code is a run that
    /// succeeded, and a 0 timestamp is the epoch. An entry that has never run must be
    /// distinguishable from an entry that ran quietly and well, because the panel shows the first
    /// as "waiting for its first run" and the second as "ran, said nothing" — and a customer
    /// debugging a job that never fires is looking at exactly that line.
    ///
    /// Null is an INFERENCE, not an identity. "All three absent" is the shape an entry that has
    /// never run produces, and it is the only evidence this contract offers, but it is not proof:
    /// the agent also reports a field as absent when what it found was unreadable rather than
    /// missing — a status file it could not parse, or a modification time before the epoch. A run
    /// whose traces were all deleted or corrupted therefore reads here as "never ran". The
    /// alternative readings are worse (a 0 exit code claims success for a run nobody observed), and
    /// the panel's own record of when it installed an entry is the second opinion worth trusting
    /// when the two disagree.
    /// </remarks>
    private static AgentCronRunOutput? ToRunOutput(GetCronEntryOutputOk ok)
    {
        if (!ok.HasOutput && !ok.HasLastExitCode && !ok.HasLastRunAtUnix)
        {
            return null;
        }

        return new AgentCronRunOutput(
            ok.HasOutput ? ok.Output : null,
            ok.HasLastExitCode ? ok.LastExitCode : null,
            ok.HasLastRunAtUnix ? ok.LastRunAtUnix : null);
    }
}
