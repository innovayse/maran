using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Common;

/// <summary>One level the settings screen offers.</summary>
/// <param name="Level">The level, as the change request names it.</param>
/// <param name="LevelDisplayName">What the level is called, in the caller's language, with what it writes.</param>
public sealed record LogLevelChoiceDto(PanelLogLevel Level, string LevelDisplayName);
