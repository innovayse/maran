using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;

namespace Maran.Modules.Tasks.Common;

/// <summary>Outward view of a <see cref="PanelTask"/>: everything a screen shows about one.</summary>
/// <remarks>
/// One DTO for the list, the single read and every frame of the stream, because a task has no
/// detail beyond this and a second near-identical type would only be somewhere for the two to drift
/// apart. The log travels with it: it is capped at the source (<see cref="PanelTask.MaxLogLength"/>),
/// so "the whole task" is a bounded payload by construction rather than by a page size chosen here.
/// </remarks>
/// <param name="Id">The task's identity, and the only identifier a request may name.</param>
/// <param name="Kind">What kind of operation it is, from <c>TaskKinds</c>.</param>
/// <param name="Subject">What it acts on — a domain, an account name.</param>
/// <param name="CorrelationId">The correlation id of the request that started it, or <c>null</c>.</param>
/// <param name="Status">Where it has got to.</param>
/// <param name="Percent">How far along, 0–100.</param>
/// <param name="Log">Everything reported about it so far, capped and marked where it was cut.</param>
/// <param name="ErrorCode">The machine-stable code it failed with, or <c>null</c>.</param>
/// <param name="StartedAt">When the operation started.</param>
/// <param name="FinishedAt">When it reached a final state, or <c>null</c> while it runs.</param>
/// <param name="Revision">
/// How many times the row has changed. The stream sends a frame only when this moves, and a client
/// that reconnects can tell a frame it has already seen from a new one.
/// </param>
public sealed record PanelTaskDto(
    Guid Id,
    string Kind,
    string Subject,
    string? CorrelationId,
    PanelTaskStatus Status,
    int Percent,
    string Log,
    string? ErrorCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Revision);
