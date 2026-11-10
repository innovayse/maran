using System.Diagnostics;
using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Jobs;
using Maran.Modules.Marketplace.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The shared front half of install, update, remove and rollback: it re-verifies the administrator's
/// password, recognises a repeated request, takes the module's operation gate, writes the audit entry,
/// begins the task and hands the work to the worker. The four command handlers differ only in what they
/// check before calling it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the design.</b> The password is checked before anything else is revealed or reserved, and
/// a wrong one is audited and counted toward lockout by <see cref="IReauthentication"/> itself. A request
/// identical to one still running is answered with that run's task and creates nothing. Only then is the
/// gate taken, so a refusal after this point never leaves a task behind; and the audit entry for the
/// accepted request is written before the task exists, so no task runs without a trace.
/// </para>
/// <para>
/// <b>Publishing after the commit.</b> The task row, the gate row and the audit entry are committed by the
/// time the worker is asked to run; the message carries no secret. If the hand-over itself fails, the task
/// is failed, the gate released and the exception continues, because an operation nobody will run must not
/// look like one that is running.
/// </para>
/// </remarks>
public sealed class ModuleOperationStarter
{
    /// <summary>Proves the administrator's password again.</summary>
    private readonly IReauthentication _reauthentication;

    /// <summary>The per-module operation gate.</summary>
    private readonly ModuleOperationGate _gate;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>The task recorder.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The request's correlation id.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Hands the accepted operation to the worker.</summary>
    private readonly IModuleOperationDispatcher _dispatcher;

    /// <summary>Creates the starter.</summary>
    /// <param name="reauthentication">Proves the administrator's password again.</param>
    /// <param name="gate">The per-module operation gate.</param>
    /// <param name="journal">The audit journal.</param>
    /// <param name="tasks">The task recorder.</param>
    /// <param name="correlationIds">The request's correlation id.</param>
    /// <param name="dispatcher">Hands the accepted operation to the worker.</param>
    public ModuleOperationStarter(
        IReauthentication reauthentication,
        ModuleOperationGate gate,
        MarketplaceAuditJournal journal,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        IModuleOperationDispatcher dispatcher)
    {
        _reauthentication = reauthentication;
        _gate = gate;
        _journal = journal;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _dispatcher = dispatcher;
    }

    /// <summary>Makes the administrator prove their password again, and audits a refusal.</summary>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="package">The module and version the entry names.</param>
    /// <param name="userId">The administrator.</param>
    /// <param name="password">The password they typed; a missing one is treated as an empty one and refused.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reauthentication failure.</returns>
    public async Task<Result> ReauthenticateAsync(
        MarketplaceOperation operation,
        ModulePackageReference package,
        Guid userId,
        string? password,
        string ipAddress,
        string userAgent,
        CancellationToken ct)
    {
        var verified = await _reauthentication.VerifyAsync(userId, password ?? string.Empty, ct);

        if (!verified.IsSuccess)
        {
            await _journal.RecordRequestAsync(operation, package, verified.Error, ipAddress, userAgent, ct);
        }

        return verified;
    }

    /// <summary>Finds the task of an identical operation that is still running.</summary>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version asked for, or the installed version a remove or rollback acts on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The existing operation's answer, or null when there is none. Read from the module's claim row, so it holds across a restart.</returns>
    public async Task<ModuleOperationAcceptedDto?> FindRepeatAsync(
        MarketplaceOperation operation,
        string moduleId,
        string? version,
        CancellationToken ct)
    {
        var same = await _gate.FindOpenAsync(moduleId, operation, version, ct);
        return same is null ? null : new ModuleOperationAcceptedDto(same.TaskId);
    }

    /// <summary>Writes the audit entry for a refusal that came after the password was proven.</summary>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="package">The module and version the entry names.</param>
    /// <param name="refusal">Why the operation was refused.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    public async Task RecordRefusalAsync(
        MarketplaceOperation operation,
        ModulePackageReference package,
        Error refusal,
        string ipAddress,
        string userAgent,
        CancellationToken ct)
    {
        await _journal.RecordRequestAsync(operation, package, refusal, ipAddress, userAgent, ct);
    }

    /// <summary>Takes the gate, audits the request, begins the task and hands the work over.</summary>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="package">The module, version and digest the entry names.</param>
    /// <param name="displayName">The catalogue's name for the module, stored with the ledger entry; null for a remove, which stages nothing, and for a rollback, which restores the earlier entry's name.</param>
    /// <param name="userId">The administrator.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The accepted answer carrying the task id, or <c>ModuleOperationInProgress</c> when another operation holds the module.</returns>
    public async Task<Result<ModuleOperationAcceptedDto>> StartAsync(
        MarketplaceOperation operation,
        ModulePackageReference package,
        string? displayName,
        Guid userId,
        string ipAddress,
        string userAgent,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);

        var claim = await _gate.AcquireAsync(package.ModuleId, ct);
        if (!claim.IsSuccess)
        {
            await _journal.RecordRequestAsync(operation, package, claim.Error, ipAddress, userAgent, ct);
            return Result<ModuleOperationAcceptedDto>.Fail(claim.Error!);
        }

        await _journal.RecordRequestAsync(operation, package, failure: null, ipAddress, userAgent, ct);

        var taskId = await _tasks.BeginAsync(
            TaskKindOf(operation),
            package.ModuleId,
            accountId: null,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            ct);

        var request = new ModuleOperationRequested(
            taskId,
            operation,
            package.ModuleId,
            package.Version,
            displayName,
            claim.Value,
            userId,
            ipAddress,
            userAgent);

        try
        {
            // The claim row names the task from here on, so a retry of this request is answered with it
            // wherever and whenever it arrives, a restart included.
            await _gate.AttachTaskAsync(claim.Value, operation, package.Version, taskId, ct);
            await _dispatcher.DispatchAsync(request, ct);
        }
        catch
        {
            // The worker will never run this, so the task must not stay open and the gate must not stay
            // held. Rethrown unchanged: an infrastructure fault is not an answer to the administrator.
            await _tasks.FailAsync(taskId, MarketplaceOperationRefusals.OperationFailed.Code, CancellationToken.None);
            await _gate.ReleaseAsync(claim.Value, CancellationToken.None);
            throw;
        }

        return Result<ModuleOperationAcceptedDto>.Ok(new ModuleOperationAcceptedDto(taskId));
    }

    /// <summary>The task kind an operation is recorded under.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The kind.</returns>
    private static string TaskKindOf(MarketplaceOperation operation)
    {
        return operation switch
        {
            MarketplaceOperation.Install => TaskKinds.ModuleInstall,
            MarketplaceOperation.Update => TaskKinds.ModuleUpdate,
            MarketplaceOperation.Remove => TaskKinds.ModuleRemove,
            MarketplaceOperation.Rollback => TaskKinds.ModuleRollback,
            _ => throw new UnreachableException("MarketplaceOperation has four members."),
        };
    }
}
