using System.Text;
using Maran.Modules.Updates.Domain.Enums;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// The one way the panel creates and withdraws a request marker in the request directory: a file whose
/// existence is the whole message (issues #382 and #516). The update request and the check request are
/// two markers made by the same code, so the exclusive creation and the "no path leaves this type"
/// rule are written once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Created exclusively.</b> <see cref="FileMode.CreateNew"/> fails when anything is already at the
/// name, so two simultaneous requests cannot both succeed and a planted link is never followed.
/// </para>
/// <para>
/// <b>No path leaves this type.</b> A failure is reported as a <see cref="PanelUpdateMarkerOutcome"/>,
/// never as an exception message, because that message names the path (rules/security.md item 8).
/// </para>
/// </remarks>
internal static class RequestMarkerFile
{
    /// <summary>Creates the marker at <paramref name="path"/>, if nothing is at its name.</summary>
    /// <param name="path">The marker's full path.</param>
    /// <param name="content">The marker's fixed content, which root never reads.</param>
    /// <returns>Whether it was created, was already there, or could not be created.</returns>
    public static PanelUpdateMarkerOutcome TryCreate(string path, string content)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.ASCII.GetBytes(content));
            return PanelUpdateMarkerOutcome.Created;
        }
        catch (IOException) when (File.Exists(path) || Directory.Exists(path))
        {
            return PanelUpdateMarkerOutcome.AlreadyPending;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PanelUpdateMarkerOutcome.Failed;
        }
    }

    /// <summary>Withdraws the marker at <paramref name="path"/>, for a request root never picked up.</summary>
    /// <param name="path">The marker's full path.</param>
    /// <returns>True when nothing is left at the marker's name afterwards.</returns>
    public static bool TryRemove(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
