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
/// <b>Created exclusively, and no path leaves this type.</b> Both are <see cref="RequestMarkerFile"/>'s,
/// the code this marker and the check marker share.
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
        return RequestMarkerFile.TryCreate(_options.MarkerPath, Content);
    }

    /// <summary>Withdraws the marker, for a request root never picked up.</summary>
    /// <returns>True when nothing is left at the marker's name afterwards.</returns>
    public bool TryRemove()
    {
        return RequestMarkerFile.TryRemove(_options.MarkerPath);
    }
}
