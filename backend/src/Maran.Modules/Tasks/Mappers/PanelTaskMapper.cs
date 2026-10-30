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
    /// <param name="kindDisplayName">
    /// The row's kind as an operator reads it, already resolved for the request's culture by
    /// <c>TaskKindDisplayNames</c>. Passed in rather than looked up here because a mapper translates
    /// and never decides (rules/csharp.md), and a resource lookup is a decision about culture.
    /// </param>
    /// <param name="includeLog">
    /// Whether the reader may see the task's log. Decided by the caller from who is reading — only
    /// an administrator may (rules/security.md item 8: the log is operator-facing English that can
    /// carry tool output, which a customer-facing answer never does). It is restated on the wire as
    /// <see cref="PanelTaskDto.LogIncluded"/>, so a reader can tell a withheld log from a task that has
    /// reported nothing yet; the log itself is then empty, so the wire shape does not change with the
    /// reader.
    /// </param>
    /// <returns>The task as a screen sees it.</returns>
    public static PanelTaskDto From(PanelTask task, string kindDisplayName, bool includeLog)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new PanelTaskDto(
            task.Id,
            task.Kind,
            kindDisplayName,
            task.Subject,
            task.CorrelationId,
            task.Status,
            task.Percent,
            includeLog ? task.Log : string.Empty,
            includeLog,
            task.ErrorCode,
            task.StartedAt,
            task.FinishedAt,
            task.Revision);
    }
}
