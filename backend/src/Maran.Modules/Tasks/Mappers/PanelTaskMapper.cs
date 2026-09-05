using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Entities;

namespace Maran.Modules.Tasks.Mappers;

/// <summary>
/// Builds a <see cref="PanelTaskDto"/> from a <see cref="PanelTask"/>. One place, because three
/// readers project the same row — the listing, the single read and every frame of the stream — and
/// three copies of the same projection are three chances for a column to be forgotten in one of them.
/// </summary>
public static class PanelTaskMapper
{
    /// <summary>Projects one task row into its outward view.</summary>
    /// <param name="task">The row to project.</param>
    /// <returns>The task as a screen sees it.</returns>
    public static PanelTaskDto From(PanelTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new PanelTaskDto(
            task.Id,
            task.Kind,
            task.Subject,
            task.CorrelationId,
            task.Status,
            task.Percent,
            task.Log,
            task.ErrorCode,
            task.StartedAt,
            task.FinishedAt,
            task.Revision);
    }
}
