using System.Text;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Domain.ValueObjects;
using Maran.Modules.Updates.Models;
using Maran.Modules.Updates.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Reads the panel's own release state from the files root maintains, as untrusted text, and the
/// request marker the panel itself creates (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here throws or echoes.</b> A file that is missing, a link, oversize, unreadable or full of
/// garbage reads as "nothing known"; a single field that fails its rule reads as absent. The panel shows
/// what it could verify and nothing else, and it never puts a path or a rejected value in an answer.
/// </para>
/// <para>
/// <b>Who wrote what.</b> <c>release.state</c>, <c>update-check.state</c> and <c>update-running.state</c>
/// are root's, in a directory the panel's account cannot write, so they are root's word. The marker is the
/// panel's own, in a root-owned directory; its age is read from the file's modification time.
/// </para>
/// </remarks>
public sealed class PanelUpdateStateReader
{
    /// <summary>The channel assumed when the release record names none, as the root CLI does.</summary>
    private const string DefaultChannel = "stable";

    /// <summary>The configured paths.</summary>
    private readonly PanelUpdateOptions _options;

    /// <summary>The panel's clock; the ambient one is a banned API.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the reader.</summary>
    /// <param name="options">Where root's files and the request directory are.</param>
    /// <param name="clock">The panel's clock, which ages the marker and the running record.</param>
    public PanelUpdateStateReader(IOptions<PanelUpdateOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Reads every fact the screen shows.</summary>
    /// <returns>The validated facts; never null, and never throws for a file problem.</returns>
    public PanelUpdateFacts Read()
    {
        var release = StateFileText.Parse(ReadSmallFile(_options.ReleaseStatePath));
        var channel = release.TryGetValue("CHANNEL", out var recorded) && PanelUpdateValueRules.IsChannel(recorded)
            ? recorded
            : DefaultChannel;

        return new PanelUpdateFacts(
            channel,
            ReadCheck(),
            ReadPrevious(release),
            ReadSignals(),
            ReadMarkerAge(_options.CheckMarkerPath),
            IsRealDirectory(_options.RequestDirectory));
    }

    /// <summary>Reads only the two facts that say whether an update is in flight.</summary>
    /// <returns>The marker's age, when there is a marker, and whether root's running record is fresh.</returns>
    public PanelUpdateSignals ReadSignals()
    {
        return new PanelUpdateSignals(ReadMarkerAge(_options.MarkerPath), IsRunningFresh());
    }

    /// <summary>Reads the last manifest check root recorded.</summary>
    /// <returns>The check, or null when there is none or any required field is not valid.</returns>
    private PanelUpdateCheck? ReadCheck()
    {
        var values = StateFileText.Parse(ReadSmallFile(_options.CheckStatePath));
        if (!values.TryGetValue("AVAILABLE", out var version)
            || !PanelUpdateValueRules.TryVersion(version, out _)
            || !values.TryGetValue("CHECKED_AT", out var checkedText)
            || !PanelUpdateValueRules.TryInstant(checkedText, out var checkedAt))
        {
            return null;
        }

        var released = values.TryGetValue("RELEASED", out var releasedText) && PanelUpdateValueRules.IsDate(releasedText)
            ? releasedText
            : null;
        var notes = values.TryGetValue("NOTES_URL", out var notesText) && PanelUpdateValueRules.IsPlainHttpsUrl(notesText)
            ? notesText
            : null;

        return new PanelUpdateCheck(checkedAt, version, released, notes);
    }

    /// <summary>Reads the release a rollback would restore, only when it is really installed.</summary>
    /// <param name="release">The parsed release record.</param>
    /// <returns>The previous release's name, or null.</returns>
    private string? ReadPrevious(IReadOnlyDictionary<string, string> release)
    {
        if (!release.TryGetValue("PREVIOUS", out var previous) || !PanelUpdateValueRules.IsReleaseName(previous))
        {
            return null;
        }

        return IsRealDirectory(Path.Combine(_options.ReleasesDirectory, previous)) ? previous : null;
    }

    /// <summary>Reads how long ago a request marker was created.</summary>
    /// <param name="path">The marker's full path.</param>
    /// <returns>The age, never negative, or null when there is no marker.</returns>
    private TimeSpan? ReadMarkerAge(string path)
    {
        try
        {
            var marker = new FileInfo(path);
            if (!marker.Exists)
            {
                return null;
            }

            var age = _clock.UtcNow - new DateTimeOffset(marker.LastWriteTimeUtc, TimeSpan.Zero);
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether root's running record exists and says it started recently enough to be believed.</summary>
    /// <returns>True when an update verb is believed to hold the lock.</returns>
    private bool IsRunningFresh()
    {
        var values = StateFileText.Parse(ReadSmallFile(_options.RunningStatePath));
        if (!values.TryGetValue("STARTED_AT", out var started) || !PanelUpdateValueRules.TryInstant(started, out var at))
        {
            return false;
        }

        var age = _clock.UtcNow - at;
        return age < PanelUpdateLedgerPolicy.RunningFreshness;
    }

    /// <summary>Whether a path is a real directory and not a link to one.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True for an existing directory that is not a symbolic link.</returns>
    private static bool IsRealDirectory(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            return directory.Exists && directory.LinkTarget is null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads a small regular file, or nothing.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>
    /// The text, or null when the file is missing, is a link, is larger than
    /// <see cref="StateFileText.MaxBytes"/>, or cannot be read.
    /// </returns>
    private static string? ReadSmallFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget is not null || info.Length > StateFileText.MaxBytes)
            {
                return null;
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
