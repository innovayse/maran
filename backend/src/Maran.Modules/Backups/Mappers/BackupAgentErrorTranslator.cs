using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// Turns the terminal event of a create stream into the machine-stable code the panel records and
/// answers with.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates; it never decides (rules/csharp.md). The decision — did this run produce an
/// artifact — has already been made by the agent and restated by the client as the event's KIND;
/// what is missing is a code, because three of the endings the client can report carry none.
/// </para>
/// <para>
/// <b>Those three endings are the reason this type exists.</b> A dropped connection, a stream that
/// went quiet, and a stream that ended without the agent stating an outcome all arrive with a null
/// error code, and every one of them is a run whose artifact may or may not be on the destination.
/// Recording them under one generic failure would tell an operator nothing about which of the three
/// happened, and they call for different next actions: a dropped connection is retried, an idle
/// stream is a stuck agent worth looking at, and a truncated stream is a partial file on disk that
/// the next listing will show as unreadable.
/// </para>
/// <para>
/// <b>What it must never do is invent a success.</b> Every kind other than
/// <see cref="BackupCreateEventKind.Created"/> maps to a failure code, including the ones that look
/// like the agent simply stopped talking. "Nothing said it failed" is not a completion.
/// </para>
/// </remarks>
public static class BackupAgentErrorTranslator
{
    /// <summary>Names the failure a non-terminal-success create event represents.</summary>
    /// <param name="terminal">The event that ended the stream.</param>
    /// <returns>
    /// The machine-stable code to record and answer with. For
    /// <see cref="BackupCreateEventKind.Failed"/> that is the agent's own translated code, which is
    /// already a resx key in the agent client's own tables and is passed through unchanged rather
    /// than folded into a code of this module's; for the endings that carry no code, one of this
    /// module's own.
    /// </returns>
    public static string ToFailureCode(BackupCreateEvent terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        return terminal.Kind switch
        {
            BackupCreateEventKind.Dropped => nameof(ErrorMessages.BackupStreamDropped),
            BackupCreateEventKind.Idle => nameof(ErrorMessages.BackupStreamIdle),
            BackupCreateEventKind.Truncated => nameof(ErrorMessages.BackupTruncated),
            BackupCreateEventKind.Cancelled => nameof(ErrorMessages.BackupCancelled),

            // Failed carries the agent's own code; Created never reaches here, and Progress is not
            // terminal. Both of those, and any kind a later client adds, fall to the truncated code
            // — the honest reading of "this stream ended and the panel cannot say it succeeded".
            _ => terminal.ErrorCode ?? nameof(ErrorMessages.BackupTruncated),
        };
    }
}
