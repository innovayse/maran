using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Creates and withdraws the check marker: what the panel's "Check for updates" hands to the root
/// check service (issue #516). It is <see cref="PanelUpdateRequestMarker"/>'s sibling, in the same
/// root-owned request directory, under its own name so the two requests start two different units.
/// </summary>
/// <remarks>
/// <b>The marker is a message by existing.</b> Its content is the fixed <see cref="Content"/> and root
/// never reads it; there is no channel, address or flag to put in it, and this type takes no argument
/// that could become one. The run it starts is the fixed <c>maran update --check-requested</c>.
/// </remarks>
public sealed class PanelUpdateCheckMarker
{
    /// <summary>The fixed content of the marker: one token and a newline.</summary>
    public const string Content = "check\n";

    /// <summary>The configured paths.</summary>
    private readonly PanelUpdateOptions _options;

    /// <summary>Creates the marker writer.</summary>
    /// <param name="options">Where the request directory is.</param>
    public PanelUpdateCheckMarker(IOptions<PanelUpdateOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Creates the marker, if nothing is at its name.</summary>
    /// <returns>Whether it was created, was already there, or could not be created.</returns>
    public PanelUpdateMarkerOutcome TryCreate()
    {
        return RequestMarkerFile.TryCreate(_options.CheckMarkerPath, Content);
    }

    /// <summary>Withdraws the marker, for a request root never picked up.</summary>
    /// <returns>True when nothing is left at the marker's name afterwards.</returns>
    public bool TryRemove()
    {
        return RequestMarkerFile.TryRemove(_options.CheckMarkerPath);
    }
}
