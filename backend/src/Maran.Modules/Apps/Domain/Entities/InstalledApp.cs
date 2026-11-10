using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;

namespace Maran.Modules.Apps.Domain.Entities;

/// <summary>
/// One app of the catalogue that the host has, or is having installed or removed, as the panel records
/// it (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>A ledger, not a probe.</b> What an app's service is doing right now is read from the agent every
/// time (<c>ListApps</c>) and is deliberately not a column here: a remembered "active" would outlive the
/// service that was stopped from a shell. What the ledger holds is what only the panel knows: that an
/// install was asked for, which task carries it, and how it ended. At start-up the ledger is reconciled
/// against the host (<c>AppLedgerPolicy</c>), and the host wins.
/// </para>
/// <para>
/// <b>One row per app.</b> The unique index on <see cref="AppId"/> is what stops two concurrent installs
/// of the same app from both starting: the loser's insert is refused. A removed app has no row; a failed
/// install keeps one so the administrator can read that it failed and ask again.
/// </para>
/// </remarks>
public sealed class InstalledApp
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The app's catalogue id as the agent and the API spell it (<c>php-8.3</c>).</summary>
    public string AppId { get; private set; }

    /// <summary>What the app is for.</summary>
    public AppKind Kind { get; private set; }

    /// <summary>The version the host reported when the app was last seen installed; empty until then.</summary>
    public string Version { get; private set; }

    /// <summary>Where the app stands.</summary>
    public AppState State { get; private set; }

    /// <summary>The task of the last install or removal, or <see langword="null"/> for an app found on the host.</summary>
    public Guid? TaskId { get; private set; }

    /// <summary>When the panel first saw the app installed, or <see langword="null"/> while it has not.</summary>
    public DateTimeOffset? InstalledAt { get; private set; }

    /// <summary>When the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether the app is installed on the host, which is what the stack gate counts.</summary>
    public bool IsInstalled
    {
        get { return State == AppState.Installed; }
    }

    /// <summary>Creates the row of an install that is about to run.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="taskId">The task that carries the install.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    private InstalledApp(Guid id, AppId appId, Guid taskId, DateTimeOffset now)
    {
        Id = id;
        AppId = appId.Value;
        Kind = appId.Kind;
        Version = string.Empty;
        State = AppState.Installing;
        TaskId = taskId;
        UpdatedAt = now;
    }

    /// <summary>Creates the row of an app found on the host that the panel did not install.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="version">The version the host reports.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    private InstalledApp(Guid id, AppId appId, string version, DateTimeOffset now)
    {
        Id = id;
        AppId = appId.Value;
        Kind = appId.Kind;
        Version = version;
        State = AppState.Installed;
        InstalledAt = now;
        UpdatedAt = now;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private InstalledApp()
    {
        AppId = string.Empty;
        Version = string.Empty;
    }

    /// <summary>Starts the ledger entry of an install.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="taskId">The task that carries the install.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>A row in the installing state.</returns>
    public static InstalledApp BeginInstall(Guid id, AppId appId, Guid taskId, DateTimeOffset now)
    {
        return new InstalledApp(id, appId, taskId, now);
    }

    /// <summary>Records an app the host has that the panel never installed (put there by hand, or by an older panel).</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="version">The version the host reports.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>A row in the installed state with no task.</returns>
    public static InstalledApp Discover(Guid id, AppId appId, string version, DateTimeOffset now)
    {
        return new InstalledApp(id, appId, version, now);
    }

    /// <summary>Starts another install of an app whose last one failed.</summary>
    /// <param name="taskId">The task that carries the new install.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    public void RetryInstall(Guid taskId, DateTimeOffset now)
    {
        State = AppState.Installing;
        TaskId = taskId;
        UpdatedAt = now;
    }

    /// <summary>Starts the removal of an installed app, or of one whose install failed part-way.</summary>
    /// <param name="taskId">The task that carries the removal.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    public void BeginRemoval(Guid taskId, DateTimeOffset now)
    {
        State = AppState.Removing;
        TaskId = taskId;
        UpdatedAt = now;
    }

    /// <summary>Records that the host has the app installed.</summary>
    /// <param name="version">The version the host reports.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    public void MarkInstalled(string version, DateTimeOffset now)
    {
        State = AppState.Installed;
        Version = version;
        InstalledAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>Records that the install ended without the app being installed, or without anyone knowing.</summary>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    public void MarkFailed(DateTimeOffset now)
    {
        State = AppState.Failed;
        UpdatedAt = now;
    }
}
