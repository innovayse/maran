using Maran.Modules.Tasks.Domain.Enums;

namespace Maran.Modules.Tasks.Domain.Entities;

/// <summary>
/// One long-running operation the panel is running or has run: what kind it is, what it acts on,
/// how far it got, and what it ended as (spec §11 — "the background task queue with progress").
/// </summary>
/// <remarks>
/// <para>
/// <b>Every rule about a task lives here rather than in whatever recorded it.</b> A task is written
/// by <c>TaskRecorder</c> on behalf of another module's handler, and that handler is in the middle
/// of doing something destructive at the time — deleting an account, replacing a certificate. It
/// must not also be the place that remembers what a legal percentage is. So the three rules below
/// are the entity's own, enforced on every call, and a caller that gets them wrong gets a corrected
/// row instead of a rejected one: refusing a progress report is refusing to record work that is
/// happening anyway.
/// </para>
/// <para>
/// 1. <b>Percent is clamped to 0–100.</b> A caller computing a percentage from a count it got wrong
/// would otherwise store a progress bar nobody can draw, and the SPA would have to defend itself
/// against the panel's own data.
/// </para>
/// <para>
/// 2. <b>The log is capped and the cut is marked.</b> The log is unbounded input in the shape of a
/// bounded field: a renewal pass over a thousand certificates, or an operation that retries, appends
/// as many lines as it likes. Past <see cref="MaxLogLength"/> the text stops growing and
/// <see cref="TruncationMarker"/> says so, because a log silently missing its end reads exactly like
/// an operation that stopped there.
/// </para>
/// <para>
/// 3. <b>A finished task never changes again.</b> The first outcome survives, the way a revoked
/// session keeps its first revocation reason. <see cref="Fail"/> after <see cref="Complete"/> is the
/// case this exists for and it is refused: a deletion whose cleanup succeeded and whose journalling
/// then threw must not be shown to an operator as a failed deletion, and the reverse — a failure
/// overwritten by a late completion — would be worse still, because it hides destruction that did
/// not finish.
/// </para>
/// <para>
/// There is no <c>AccountId</c> and no tenant column, deliberately. Every kind recorded in v1 is an
/// administrator's operation and the whole surface is admin-only (<c>TasksDbContext</c>); a
/// customer-facing feed arrives with the modules whose operations a customer starts, and it will
/// need a tenant column added with it rather than inherited from a guess made now.
/// </para>
/// </remarks>
public sealed class PanelTask
{
    /// <summary>How much log text a task keeps before it stops growing.</summary>
    /// <remarks>
    /// Sized for an operator reading a pane, not for an archive: 16 KiB is far more than any
    /// instrumented operation writes, and small enough that a thousand finished tasks are a
    /// megabyte of table rather than a gigabyte.
    /// </remarks>
    public const int MaxLogLength = 16384;

    /// <summary>What is appended in place of the text that did not fit.</summary>
    /// <remarks>
    /// Operator-facing English and not localized (rules/csharp.md): it is part of the log's own
    /// text, which is the operator's diagnostic material, not a message the panel renders to a
    /// customer.
    /// </remarks>
    public const string TruncationMarker = "\n[log truncated]";

    /// <summary>The row's identity, and the only identifier a request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>What kind of operation this is, from <c>TaskKinds</c>.</summary>
    public string Kind { get; private set; }

    /// <summary>What the operation acts on — a domain, an account name — as an operator searches for it.</summary>
    public string Subject { get; private set; }

    /// <summary>The correlation id of the request that started it, or <c>null</c> for unattended work.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Where the operation has got to.</summary>
    public PanelTaskStatus Status { get; private set; }

    /// <summary>How far along it is, 0–100.</summary>
    public int Percent { get; private set; }

    /// <summary>Everything reported about it so far, one line per report, capped.</summary>
    public string Log { get; private set; }

    /// <summary>The machine-stable code it failed with, or <c>null</c> while it has not failed.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>When the operation started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it reached a final state, or <c>null</c> while it is still running.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>
    /// How many times this row has changed, counting from zero at creation.
    /// </summary>
    /// <remarks>
    /// The stream's whole reason for existing: a watching client is sent a frame when — and only
    /// when — this number moves, so a task that is running but not progressing costs the connection
    /// its heartbeat comments and nothing else. Comparing whole rows instead would send a frame per
    /// poll, or would need the reader to define equality over a growing log.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>Opens a task for an operation that is starting.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="kind">What kind of operation this is, from <c>TaskKinds</c>.</param>
    /// <param name="subject">What the operation acts on.</param>
    /// <param name="correlationId">The correlation id of the request behind it, or <c>null</c>.</param>
    /// <param name="startedAt">The starting instant, taken from <see cref="IClock"/>.</param>
    public PanelTask(Guid id, string kind, string subject, string? correlationId, DateTimeOffset startedAt)
    {
        Id = id;
        Kind = kind;
        Subject = subject;
        CorrelationId = correlationId;
        Status = PanelTaskStatus.Running;
        Percent = 0;
        Log = string.Empty;
        StartedAt = startedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PanelTask()
    {
        Kind = string.Empty;
        Subject = string.Empty;
        Log = string.Empty;
    }

    /// <summary>Records one stage of the operation: how far it has got, and one line about it.</summary>
    /// <param name="percent">How far along, clamped to 0–100.</param>
    /// <param name="line">One line of operator-facing English, appended to the log within its cap.</param>
    /// <remarks>
    /// A report against a finished task is ignored rather than applied. Progress arriving after an
    /// outcome is either a late write from work that has already been accounted for, or a bug in an
    /// instrumented handler; applying it would move a finished task's bar and make the row disagree
    /// with its own status.
    /// </remarks>
    public void Report(int percent, string line)
    {
        if (Status != PanelTaskStatus.Running)
        {
            return;
        }

        Percent = Math.Clamp(percent, 0, 100);
        Append(line);
        Revision += 1;
    }

    /// <summary>Closes the task as having finished its work.</summary>
    /// <param name="at">The finishing instant, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// <para>Ignored once the task has already finished: the first outcome is the one that stands.</para>
    /// <para>
    /// <b><see cref="Percent"/> becomes 100 because the operation reached its end, not because
    /// anything measured a hundred percent of anything.</b> A percent on this row is a stage marker
    /// the instrumented operation chose, and the meaning of the completion is carried by the
    /// operation's own log lines and by what it checked before calling this — never by the number.
    /// It is written here rather than left where the last report put it so that a finished task
    /// cannot render as a bar stuck at ninety, which reads as an operation that stopped.
    /// </para>
    /// </remarks>
    public void Complete(DateTimeOffset at)
    {
        if (Status != PanelTaskStatus.Running)
        {
            return;
        }

        Status = PanelTaskStatus.Completed;
        Percent = 100;
        FinishedAt = at;
        Revision += 1;
    }

    /// <summary>Closes the task as not having finished its work, under the code it answered with.</summary>
    /// <param name="errorCode">The machine-stable code the operation returned to its caller.</param>
    /// <param name="at">The finishing instant, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// Ignored once the task has already finished, which is the rule this method exists to state:
    /// a failure reported after a completion is refused, and the completed task stays completed.
    /// </remarks>
    public void Fail(string errorCode, DateTimeOffset at)
    {
        if (Status != PanelTaskStatus.Running)
        {
            return;
        }

        Status = PanelTaskStatus.Failed;
        ErrorCode = errorCode;
        FinishedAt = at;
        Revision += 1;
    }

    /// <summary>Appends one line to the log, cutting and marking it once it no longer fits.</summary>
    /// <param name="line">The line to append, without its trailing newline.</param>
    /// <remarks>
    /// Once the marker is on the end, the log is longer than the cap and every later line is
    /// dropped in one comparison — so a task that keeps reporting costs one length check per report
    /// rather than a growing string it then throws away.
    /// </remarks>
    private void Append(string line)
    {
        if (Log.Length > MaxLogLength)
        {
            return;
        }

        Log = Log.Length == 0 ? line : Log + "\n" + line;
        if (Log.Length > MaxLogLength)
        {
            Log = Log[..MaxLogLength] + TruncationMarker;
        }
    }
}
