namespace Maran.Modules.Files.Options;

/// <summary>
/// The file manager's limits. Bound from the <c>Files</c> configuration section, <c>Files__*</c> in
/// <c>panel.env</c>, and returned by <c>GET api/v1/files/options</c> so the SPA holds no constant.
/// </summary>
/// <remarks>
/// <para>
/// Every number is validated at startup by <c>FilesOptionsValidator</c>, so a value that would
/// silently disable a protection refuses the boot (rules/csharp.md "Options validated at startup").
/// </para>
/// <para>
/// <b>The extraction limits are sent to the agent with each request and enforced there.</b> The panel
/// shows them and passes them; it never claims to enforce them itself. The agent applies its own
/// ceiling above whatever is sent.
/// </para>
/// </remarks>
public sealed class FilesOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Files";

    /// <summary>The most bytes an upload may carry. 100 MiB.</summary>
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>The most bytes a file may be for the text editor to open it. 1 MiB.</summary>
    public long MaxEditBytes { get; set; } = 1024 * 1024;

    /// <summary>The most bytes a download serves; the agent refuses a larger file before streaming. 256 MiB.</summary>
    public long MaxDownloadBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>The most items one batch request may name.</summary>
    public int MaxBatchItems { get; set; } = 200;

    /// <summary>How many days an entry stays in the trash before the retention job removes it.</summary>
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>The most entries an extraction may write.</summary>
    public long ExtractMaxEntries { get; set; } = 100_000;

    /// <summary>The most bytes an extraction may write. 2 GiB.</summary>
    public long ExtractMaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>The highest accepted ratio of expanded bytes to the archive's own size, as a whole number.</summary>
    public int ExtractMaxRatio { get; set; } = 100;

    /// <summary>The most transfers (uploads and downloads) one account may have in flight at once.</summary>
    public int MaxConcurrentTransfersPerAccount { get; set; } = 3;

    /// <summary>The most transfers the whole panel may have in flight at once, across every account.</summary>
    public int MaxConcurrentTransfersGlobal { get; set; } = 16;
}
