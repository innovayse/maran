namespace Maran.Modules.Updates.Common;

/// <summary>
/// What <c>GET /api/v1/panel-update</c> answers: the facts of the running release and of the last check.
/// Facts only — every sentence about the outcome of an update lives in the task list.
/// </summary>
/// <param name="CurrentVersion">The version that is running.</param>
/// <param name="Channel">The release channel the panel follows.</param>
/// <param name="Available">The newer release the last check found, or null when the panel is current or was never checked.</param>
/// <param name="LastCheckedAt">When the last manifest fetch by the root CLI ran, or null when none has.</param>
/// <param name="PreviousVersion">The version a rollback would restore, or null when no rollback is possible.</param>
/// <param name="UpdateInProgress">True while an update is queued or running.</param>
/// <param name="CheckInProgress">True while a release check the panel asked for is queued or running (issue #516).</param>
/// <param name="CanStart">Whether an update can be handed to the root update service on this server.</param>
public sealed record PanelUpdateDto(
    string CurrentVersion,
    string Channel,
    PanelUpdateAvailableDto? Available,
    DateTimeOffset? LastCheckedAt,
    string? PreviousVersion,
    bool UpdateInProgress,
    bool CheckInProgress,
    bool CanStart);
