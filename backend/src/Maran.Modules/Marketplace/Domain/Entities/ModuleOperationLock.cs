using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Domain.Entities;

/// <summary>
/// A claim that one operation (install, update, remove, rollback) is in progress on one module. A unique
/// index on <see cref="ModuleId"/> makes a second claim on the same module a constraint violation rather
/// than a race somebody has to remember to check. The claim also names the operation and its task once the
/// task exists, so the row is the one record of what is open: it survives a restart and is the same for
/// every process.
/// </summary>
/// <remarks>
/// <b>A claim can be abandoned</b> (the process died between acquiring and releasing), so it expires: a
/// claim older than <see cref="StaleAfter"/> is one the next caller takes over. The expiry is an upper
/// bound on one operation, which downloads, verifies and stages a package and never waits on a person.
/// </remarks>
public sealed class ModuleOperationLock
{
    /// <summary>How long a claim stands before the next caller may take it over.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>The claim's identity. Release addresses it, so a taken-over claim's old owner cannot release the new one.</summary>
    public Guid Id { get; private set; }

    /// <summary>The module the operation is on; unique across all claims.</summary>
    public string ModuleId { get; private set; }

    /// <summary>When the claim was made.</summary>
    public DateTimeOffset AcquiredAt { get; private set; }

    /// <summary>
    /// Which operation the claim carries; null from the moment of the claim until <see cref="Attach"/> names
    /// it, and for a claim made before this column existed.
    /// </summary>
    public MarketplaceOperation? Operation { get; private set; }

    /// <summary>
    /// The version the operation installs, or the installed version a remove or rollback acts on; null while
    /// <see cref="Operation"/> is null, and for an operation that names none.
    /// </summary>
    public string? Version { get; private set; }

    /// <summary>The task that carries the operation's progress; null while <see cref="Operation"/> is null.</summary>
    public Guid? TaskId { get; private set; }

    /// <summary>Makes a claim on a module.</summary>
    /// <param name="id">The claim's identity.</param>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="at">The instant, taken from <see cref="IClock"/>.</param>
    public ModuleOperationLock(Guid id, string moduleId, DateTimeOffset at)
    {
        Id = id;
        ModuleId = moduleId;
        AcquiredAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ModuleOperationLock()
    {
        ModuleId = string.Empty;
    }

    /// <summary>
    /// Records which operation, on which version, the claim's task carries, so a repeat of the same request
    /// is answered with that task even by another process or after a restart.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="version">The version it installs, or the installed version a remove or rollback acts on.</param>
    /// <param name="taskId">The task that carries its progress.</param>
    public void Attach(MarketplaceOperation operation, string? version, Guid taskId)
    {
        Operation = operation;
        Version = version;
        TaskId = taskId;
    }

    /// <summary>Tells whether the claim carries exactly this operation on exactly this version, with a task.</summary>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="version">The version asked for, or the installed version a remove or rollback acts on.</param>
    /// <returns>True when a repeat of the request is the operation this claim already carries.</returns>
    public bool CarriesSame(MarketplaceOperation operation, string? version)
    {
        return TaskId is not null
            && Operation == operation
            && string.Equals(Version, version, StringComparison.Ordinal);
    }

    /// <summary>Tells whether the claim is old enough that its owner is presumed gone.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>True when the claim is older than <see cref="StaleAfter"/>.</returns>
    public bool IsStale(DateTimeOffset now)
    {
        return now - AcquiredAt > StaleAfter;
    }
}
