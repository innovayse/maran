using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Mappers;

/// <summary>Restates an effective dashboard layout on the wire.</summary>
public static class DashboardLayoutMapper
{
    /// <summary>Projects the effective lists into their outward view.</summary>
    /// <param name="widgets">The effective widget list, in drawing order.</param>
    /// <param name="actions">The effective shortcut list, in drawing order.</param>
    /// <returns>The layout the dashboard reads.</returns>
    public static DashboardLayoutDto From(
        IEnumerable<DashboardLayoutEntry> widgets,
        IEnumerable<DashboardLayoutEntry> actions)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(actions);

        return new DashboardLayoutDto(Entries(widgets), Entries(actions));
    }

    /// <summary>Projects one list.</summary>
    /// <param name="entries">The list's entries.</param>
    /// <returns>The same entries as wire shapes, in the same order.</returns>
    private static List<DashboardLayoutEntryDto> Entries(IEnumerable<DashboardLayoutEntry> entries)
    {
        return entries
            .Select(entry =>
            {
                return new DashboardLayoutEntryDto(entry.Key, entry.Hidden);
            })
            .ToList();
    }
}
