using Maran.Modules.Updates.Options;
using Maran.Modules.Updates.Services;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>
/// An isolated directory standing in for the server: the request directory the installer builds, the
/// releases directory, root's three files and the panel's ledger, all under a temporary root, with the
/// real reader, marker and ledger over them.
/// </summary>
public sealed class UpdatesWorld : IDisposable
{
    /// <summary>The temporary root everything lives under.</summary>
    private readonly string _root;

    /// <summary>The paths every service reads.</summary>
    public PanelUpdateOptions Options { get; }

    /// <summary>The clock every service reads.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>The real reader.</summary>
    public PanelUpdateStateReader Reader { get; }

    /// <summary>The real marker writer.</summary>
    public PanelUpdateRequestMarker Marker { get; }

    /// <summary>The real check marker writer (issue #516).</summary>
    public PanelUpdateCheckMarker CheckMarker { get; }

    /// <summary>The real ledger.</summary>
    public PanelUpdateTaskLedger Ledger { get; }

    /// <summary>Builds the directories and the services over them.</summary>
    /// <param name="createRequestDirectory">Whether the installer's request directory exists; false stands in for a server whose path unit is not installed.</param>
    public UpdatesWorld(bool createRequestDirectory = true)
    {
        _root = Directory.CreateTempSubdirectory("maran-updates-tests-").FullName;
        Options = new PanelUpdateOptions
        {
            ReleaseStatePath = Path.Combine(_root, "release.state"),
            CheckStatePath = Path.Combine(_root, "update-check.state"),
            RunningStatePath = Path.Combine(_root, "update-running.state"),
            ReleasesDirectory = Path.Combine(_root, "releases"),
            RequestDirectory = Path.Combine(_root, "request"),
            RequestMarkerName = "requested",
            CheckRequestMarkerName = "check-requested",
            TaskLedgerPath = Path.Combine(_root, "panel-update-task"),
        };
        Directory.CreateDirectory(Options.ReleasesDirectory);
        if (createRequestDirectory)
        {
            Directory.CreateDirectory(Options.RequestDirectory);
        }

        Reader = new PanelUpdateStateReader(new OptionsWrapper<PanelUpdateOptions>(Options), Clock);
        Marker = new PanelUpdateRequestMarker(new OptionsWrapper<PanelUpdateOptions>(Options));
        CheckMarker = new PanelUpdateCheckMarker(new OptionsWrapper<PanelUpdateOptions>(Options));
        Ledger = new PanelUpdateTaskLedger(new OptionsWrapper<PanelUpdateOptions>(Options), Clock);
    }

    /// <summary>Writes root's release record.</summary>
    /// <param name="text">The file's text.</param>
    public void WriteReleaseState(string text)
    {
        File.WriteAllText(Options.ReleaseStatePath, text);
    }

    /// <summary>Writes root's last-check record.</summary>
    /// <param name="text">The file's text.</param>
    public void WriteCheckState(string text)
    {
        File.WriteAllText(Options.CheckStatePath, text);
    }

    /// <summary>Writes root's running record.</summary>
    /// <param name="text">The file's text.</param>
    public void WriteRunningState(string text)
    {
        File.WriteAllText(Options.RunningStatePath, text);
    }

    /// <summary>Installs a release directory, as the installer's layout does.</summary>
    /// <param name="name">The release's name.</param>
    public void InstallRelease(string name)
    {
        Directory.CreateDirectory(Path.Combine(Options.ReleasesDirectory, name));
    }

    /// <summary>Plants a request marker of a given age.</summary>
    /// <param name="age">How long ago it was created.</param>
    public void PlantMarker(TimeSpan age)
    {
        File.WriteAllText(Options.MarkerPath, "update\n");
        File.SetLastWriteTimeUtc(Options.MarkerPath, Clock.UtcNow.UtcDateTime - age);
    }

    /// <summary>Plants a check request marker of a given age.</summary>
    /// <param name="age">How long ago it was created.</param>
    public void PlantCheckMarker(TimeSpan age)
    {
        File.WriteAllText(Options.CheckMarkerPath, "check\n");
        File.SetLastWriteTimeUtc(Options.CheckMarkerPath, Clock.UtcNow.UtcDateTime - age);
    }

    /// <summary>Ages the ledger file.</summary>
    /// <param name="age">How long ago it was written.</param>
    public void AgeLedger(TimeSpan age)
    {
        File.SetLastWriteTimeUtc(Options.TaskLedgerPath, Clock.UtcNow.UtcDateTime - age);
    }

    /// <summary>Whether the request marker exists.</summary>
    /// <returns>True when something is at the marker's name.</returns>
    public bool MarkerExists()
    {
        return File.Exists(Options.MarkerPath);
    }

    /// <summary>Whether the check request marker exists.</summary>
    /// <returns>True when something is at the check marker's name.</returns>
    public bool CheckMarkerExists()
    {
        return File.Exists(Options.CheckMarkerPath);
    }

    /// <summary>Formats an instant the way root writes it.</summary>
    /// <param name="ago">How long before the clock's now.</param>
    /// <returns>The <c>yyyy-MM-ddTHH:mm:ssZ</c> text.</returns>
    public string Instant(TimeSpan ago)
    {
        return (Clock.UtcNow - ago).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
