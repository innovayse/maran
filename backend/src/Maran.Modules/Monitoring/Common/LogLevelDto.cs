using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Common;

/// <summary>The panel's log level as the settings screen shows it (issue #131).</summary>
/// <param name="Level">The level the running logger writes at — read from the logger, not from storage.</param>
/// <param name="LevelDisplayName">The running level's name in the caller's language.</param>
/// <param name="Choices">Every level an administrator may choose, each with its name in the caller's language.</param>
public sealed record LogLevelDto(
    PanelLogLevel Level,
    string LevelDisplayName,
    IReadOnlyList<LogLevelChoiceDto> Choices);
