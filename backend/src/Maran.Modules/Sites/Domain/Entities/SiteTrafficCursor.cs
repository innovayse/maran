namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>Where the last summarising read of a site's access log stopped, so the next one starts there.</summary>
/// <remarks>
/// <para>
/// One row per site. The cursor is the only stateful idea of the traffic summary: it lets an hourly pass
/// read each log line once instead of re-reading a file of unbounded size, and it is moved in the SAME
/// transaction that adds the read's counts to the day rows, so a failed write leaves the old cursor and
/// the next pass re-reads rather than counting twice or skipping.
/// </para>
/// <para>
/// When the file the cursor names is gone (rotated twice between two passes, or replaced) the agent says
/// so and the lines between are lost; the panel does not guess them back. <see cref="IncompleteSince"/>
/// records when that was last seen, and the summary says its figures may be incomplete from then on.
/// </para>
/// </remarks>
public sealed class SiteTrafficCursor
{
    /// <summary>The site whose log the cursor belongs to.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The site, as the relationship EF Core cascades on and the tenant filter reads the account through.</summary>
    public Site Site { get; private set; }

    /// <summary>The identity of the file <see cref="Offset"/> belongs to, in the agent's own spelling; opaque to the panel.</summary>
    public string FileId { get; private set; }

    /// <summary>The byte offset in that file where the next read starts.</summary>
    public long Offset { get; private set; }

    /// <summary>The instant the cursor last moved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The instant the agent last reported lost lines, or null when it never has.</summary>
    public DateTimeOffset? IncompleteSince { get; private set; }

    /// <summary>Creates the cursor of a site that has just been read for the first time.</summary>
    /// <param name="site">The site whose log the cursor belongs to.</param>
    /// <param name="fileId">The identity of the file the offset belongs to.</param>
    /// <param name="offset">The byte offset where the next read starts.</param>
    /// <param name="at">The instant the cursor was set, taken from <see cref="IClock"/>.</param>
    public SiteTrafficCursor(Site site, string fileId, long offset, DateTimeOffset at)
    {
        Site = site;
        SiteId = site.Id;
        FileId = fileId;
        Offset = offset;
        UpdatedAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteTrafficCursor()
    {
        Site = null!;
        FileId = string.Empty;
    }

    /// <summary>Moves the cursor to where the latest read stopped.</summary>
    /// <param name="fileId">The identity of the file the offset belongs to.</param>
    /// <param name="offset">The byte offset where the next read starts.</param>
    /// <param name="at">The instant the cursor moved, taken from <see cref="IClock"/>.</param>
    public void Advance(string fileId, long offset, DateTimeOffset at)
    {
        FileId = fileId;
        Offset = offset;
        UpdatedAt = at;
    }

    /// <summary>Records that the agent reported lines it could not read, so the figures may be short.</summary>
    /// <param name="at">The instant the loss was seen, taken from <see cref="IClock"/>.</param>
    public void RecordGap(DateTimeOffset at)
    {
        IncompleteSince = at;
    }
}
