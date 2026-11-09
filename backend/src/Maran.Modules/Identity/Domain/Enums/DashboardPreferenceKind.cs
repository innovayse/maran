namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Which of the dashboard's two ordered lists a preference belongs to. Stored as its name.</summary>
public enum DashboardPreferenceKind
{
    /// <summary>A widget the dashboard draws.</summary>
    Widget,

    /// <summary>A shortcut of the quick-actions bar.</summary>
    Action,
}
