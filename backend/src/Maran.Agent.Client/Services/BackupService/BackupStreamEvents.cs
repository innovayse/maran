namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Builds the payload-free events a backup stream ends or fails with, so no stream spells them twice.</summary>
internal static class BackupStreamEvents
{
    /// <summary>Builds a terminal create event with no payload, for the endings that carry none.</summary>
    /// <param name="kind">Which ending this is.</param>
    /// <returns>The event that closes the sequence.</returns>
    public static BackupCreateEvent CreateEnding(BackupCreateEventKind kind)
    {
        return new BackupCreateEvent(kind, 0, string.Empty, 0, string.Empty, 0, null);
    }

    /// <summary>Builds the create event that reports a failure by its code.</summary>
    /// <param name="errorCode">The machine code of the failure.</param>
    /// <returns>The failed event that closes the sequence.</returns>
    public static BackupCreateEvent CreateFailed(string errorCode)
    {
        return new BackupCreateEvent(BackupCreateEventKind.Failed, 0, string.Empty, 0, string.Empty, 0, errorCode);
    }

    /// <summary>Builds a terminal restore event with no payload, for the endings that carry none.</summary>
    /// <param name="kind">Which ending this is.</param>
    /// <returns>The event that closes the sequence.</returns>
    public static BackupRestoreEvent RestoreEnding(BackupRestoreEventKind kind)
    {
        return new BackupRestoreEvent(kind, 0, string.Empty, null, null);
    }
}
