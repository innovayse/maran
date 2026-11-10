namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>Where an app stands in the panel's ledger.</summary>
public enum AppState
{
    /// <summary>An install is running as a task.</summary>
    Installing = 1,

    /// <summary>The app is installed on the host.</summary>
    Installed = 2,

    /// <summary>The last install ended without the app being installed, or its outcome is not known.</summary>
    Failed = 3,

    /// <summary>A removal is running as a task.</summary>
    Removing = 4,
}
