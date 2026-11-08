using System.Globalization;
using System.Text;
using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// The panel's own note of which task an update request opened (issue #382). The update restarts the
/// panel, so the process that reads the outcome is not the process that opened the task; this file is
/// what carries the task's id across that restart.
/// </summary>
/// <remarks>
/// <para>
/// One line holding a task id, in a file only the panel's account writes. Reading it is lenient in the
/// way the rest of the module is: a missing, oversize or garbled file means "there is no open request"
/// and never an exception. It is written beside and renamed in, so a reader never sees half a line.
/// </para>
/// <para>
/// Its modification time is the age the reaper judges: the ledger is written once per request, so the
/// time is the request's.
/// </para>
/// </remarks>
public sealed class PanelUpdateTaskLedger
{
    /// <summary>The most bytes a ledger may hold and still be read: a Guid and a newline fit in far less.</summary>
    private const int MaxBytes = 128;

    /// <summary>The configured paths.</summary>
    private readonly PanelUpdateOptions _options;

    /// <summary>The panel's clock, which ages the ledger.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the ledger.</summary>
    /// <param name="options">Where the ledger file is.</param>
    /// <param name="clock">The panel's clock.</param>
    public PanelUpdateTaskLedger(IOptions<PanelUpdateOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Records the task an update request opened, and only when no other request's task is open.</summary>
    /// <param name="taskId">The task's id.</param>
    /// <returns>
    /// <see cref="PanelUpdateLedgerOutcome.Written"/> when the ledger now names the task;
    /// <see cref="PanelUpdateLedgerOutcome.AlreadyOpen"/> when a ledger already exists, which is left exactly
    /// as it was; <see cref="PanelUpdateLedgerOutcome.Failed"/> when it could not be written.
    /// </returns>
    /// <remarks>
    /// The move refuses to replace, so the ledger is also what makes two simultaneous requests safe: the
    /// one that loses never touches the winner's ledger, and so can never remove it when it takes back its
    /// own task.
    /// </remarks>
    public PanelUpdateLedgerOutcome TryWrite(Guid taskId)
    {
        var temporary = _options.TaskLedgerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, taskId.ToString("D", CultureInfo.InvariantCulture) + "\n", Encoding.ASCII);
            File.Move(temporary, _options.TaskLedgerPath, overwrite: false);
            return PanelUpdateLedgerOutcome.Written;
        }
        catch (IOException) when (File.Exists(_options.TaskLedgerPath))
        {
            TryDelete(temporary);
            return PanelUpdateLedgerOutcome.AlreadyOpen;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return PanelUpdateLedgerOutcome.Failed;
        }
    }

    /// <summary>Reads the task id the ledger names.</summary>
    /// <returns>The id, or null when there is no ledger or it does not hold exactly one task id.</returns>
    public Guid? TryRead()
    {
        try
        {
            var info = new FileInfo(_options.TaskLedgerPath);
            if (!info.Exists || info.LinkTarget is not null || info.Length > MaxBytes)
            {
                return null;
            }

            var text = File.ReadAllText(_options.TaskLedgerPath, Encoding.ASCII).Trim();
            return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>How long ago the ledger was written.</summary>
    /// <returns>The age, never negative, or null when there is no ledger file at all (garbled or not).</returns>
    public TimeSpan? Age()
    {
        try
        {
            var info = new FileInfo(_options.TaskLedgerPath);
            if (!info.Exists)
            {
                return null;
            }

            var age = _clock.UtcNow - new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the ledger, once the request it names has ended.</summary>
    public void Remove()
    {
        TryDelete(_options.TaskLedgerPath);
    }

    /// <summary>Deletes a file and ignores the failure of doing so.</summary>
    /// <param name="path">The file to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A ledger that cannot be removed is judged again by the next pass; there is nothing
            // more useful to do with the failure, and a path must not be logged (rules/security.md item 8).
        }
    }
}
