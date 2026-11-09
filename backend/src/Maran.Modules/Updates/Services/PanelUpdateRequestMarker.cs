using System.Text;
using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Creates and withdraws the request marker: the one thing the panel hands to the root update service
/// (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>The marker is a message by existing.</b> Its content is the fixed <see cref="Content"/> and root
/// never reads it; there is no version, address, path or flag to put in it, and this type takes no
/// argument that could become one.
/// </para>
/// <para>
/// <b>Created exclusively.</b> <see cref="FileMode.CreateNew"/> fails when anything is already at the
/// name, so two simultaneous requests cannot both succeed and a planted link is never followed.
/// </para>
/// <para>
/// <b>No path leaves this type.</b> A failure is reported as a <see cref="PanelUpdateMarkerOutcome"/>,
/// never as an exception message, because that message names the path (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class PanelUpdateRequestMarker
{
    /// <summary>The fixed content of the marker: one token and a newline.</summary>
    public const string Content = "update\n";

    /// <summary>The configured paths.</summary>
    private readonly PanelUpdateOptions _options;

    /// <summary>Creates the marker writer.</summary>
    /// <param name="options">Where the request directory is.</param>
    public PanelUpdateRequestMarker(IOptions<PanelUpdateOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Creates the marker, if nothing is at its name.</summary>
    /// <returns>Whether it was created, was already there, or could not be created.</returns>
    public PanelUpdateMarkerOutcome TryCreate()
    {
        try
        {
            using var stream = new FileStream(_options.MarkerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.ASCII.GetBytes(Content));
            return PanelUpdateMarkerOutcome.Created;
        }
        catch (IOException) when (File.Exists(_options.MarkerPath) || Directory.Exists(_options.MarkerPath))
        {
            return PanelUpdateMarkerOutcome.AlreadyPending;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PanelUpdateMarkerOutcome.Failed;
        }
    }

    /// <summary>Withdraws the marker, for a request root never picked up.</summary>
    /// <returns>True when nothing is left at the marker's name afterwards.</returns>
    public bool TryRemove()
    {
        try
        {
            File.Delete(_options.MarkerPath);
            return !File.Exists(_options.MarkerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
