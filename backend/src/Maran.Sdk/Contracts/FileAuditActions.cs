namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for the file manager. Constants rather than an enum: a
/// marketplace module records actions this assembly was never compiled knowing about, and an enum
/// could not be extended from outside.
/// </summary>
/// <remarks>
/// <para>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </para>
/// <para>
/// <b>The subject of every entry is the account's name and the validated relative path, and NEVER the
/// file's content.</b> The journal is append-only and never deleted; a path is the customer's own text
/// and is kept because an operator cannot investigate a deletion without it.
/// </para>
/// </remarks>
public static class FileAuditActions
{
    /// <summary>An empty file or a folder was created.</summary>
    public const string FileCreated = "FileCreated";

    /// <summary>A file's text was saved from the editor. Subject: the path, never the text.</summary>
    public const string FileSaved = "FileSaved";

    /// <summary>An upload was opened. Written at stream open with the size the caller declared.</summary>
    public const string FileUploadStarted = "FileUploadStarted";

    /// <summary>An upload completed. Written with the byte count the panel counted itself.</summary>
    public const string FileUploaded = "FileUploaded";

    /// <summary>An upload ended without completing; the agent keeps no partial file.</summary>
    public const string FileUploadAborted = "FileUploadAborted";

    /// <summary>A download was opened.</summary>
    public const string FileDownloadStarted = "FileDownloadStarted";

    /// <summary>A download was streamed to its end.</summary>
    public const string FileDownloadCompleted = "FileDownloadCompleted";

    /// <summary>A download ended before its end.</summary>
    public const string FileDownloadAborted = "FileDownloadAborted";

    /// <summary>An entry was renamed within its folder.</summary>
    public const string FileRenamed = "FileRenamed";

    /// <summary>An entry was moved to another folder. One entry per item of a batch.</summary>
    public const string FileMoved = "FileMoved";

    /// <summary>An entry was copied to another folder. One entry per item of a batch.</summary>
    public const string FileCopied = "FileCopied";

    /// <summary>An entry's permission bits were changed; a recursive change is one entry.</summary>
    public const string FilePermissionsChanged = "FilePermissionsChanged";

    /// <summary>An entry was moved to the trash. One entry per item of a batch.</summary>
    public const string FileTrashed = "FileTrashed";

    /// <summary>A trashed entry was restored. One entry per item of a batch.</summary>
    public const string FileRestored = "FileRestored";

    /// <summary>The account's trash was emptied for good.</summary>
    public const string FileTrashEmptied = "FileTrashEmptied";

    /// <summary>An entry was deleted permanently, bypassing the trash. One entry per item of a batch.</summary>
    public const string FileDeleted = "FileDeleted";

    /// <summary>An archive was created.</summary>
    public const string FileArchived = "FileArchived";

    /// <summary>An archive was extracted.</summary>
    public const string FileExtracted = "FileExtracted";

    /// <summary>The scheduled retention job removed expired trash of an account.</summary>
    public const string FileTrashRetentionPurged = "FileTrashRetentionPurged";

    /// <summary>
    /// A read was refused before any file was touched: another account's id, or a suspended account.
    /// Recorded so a cross-tenant probe on a read route leaves the same trace a mutation does.
    /// </summary>
    public const string FileAccessRefused = "FileAccessRefused";
}
