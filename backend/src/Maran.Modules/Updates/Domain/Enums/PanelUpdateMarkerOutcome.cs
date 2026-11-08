namespace Maran.Modules.Updates.Domain.Enums;

/// <summary>What happened when the panel tried to create its update request marker.</summary>
public enum PanelUpdateMarkerOutcome
{
    /// <summary>The marker was created; root will see it.</summary>
    Created,

    /// <summary>Something was already at the marker's name: another request is waiting.</summary>
    AlreadyPending,

    /// <summary>The marker could not be created (the directory is missing or not writable).</summary>
    Failed,
}
