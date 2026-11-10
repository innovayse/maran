namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>The two long-running operations the app store runs as tasks.</summary>
public enum AppOperation
{
    /// <summary>Installing an app.</summary>
    Install = 1,

    /// <summary>Removing an app.</summary>
    Uninstall = 2,
}
