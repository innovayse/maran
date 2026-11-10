using Maran.Modules.Updates.Domain.ValueObjects;

namespace Maran.Modules.Updates.Models;

/// <summary>
/// Everything the panel can learn about its own release state from the files root maintains, read at one
/// instant and already validated.
/// </summary>
/// <param name="Channel">The release channel the panel follows; <c>stable</c> when the record says nothing valid.</param>
/// <param name="Check">The last manifest check, or null when there is none or it was not valid.</param>
/// <param name="PreviousVersion">The release a rollback would restore, or null when none is installed.</param>
/// <param name="Signals">What the file system shows about an update in flight.</param>
/// <param name="CheckMarkerAge">How long ago the panel's check request marker was created, or null when there is none: the marker exists from the request until the root check service has finished (issue #516).</param>
/// <param name="CanStart">Whether the request directory the installer builds exists, so a request can be handed to root.</param>
public sealed record PanelUpdateFacts(
    string Channel,
    PanelUpdateCheck? Check,
    string? PreviousVersion,
    PanelUpdateSignals Signals,
    TimeSpan? CheckMarkerAge,
    bool CanStart);
