namespace Maran.SharedKernel.Utilities.IO;

/// <summary>
/// Writes bytes to a file so a reader can never observe a partial result: a temp file in the SAME
/// directory as the target, written and fsynced, then renamed over the target in one atomic
/// filesystem operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in SharedKernel, not one module.</b> "Write a file so a crash mid-write can
/// never leave a half-written artefact readable" is not a Licensing-specific problem — it is the same
/// hazard <c>installer/lib/60-config.sh</c> solves for <c>panel.env</c> — and a module may not import
/// another module's types (rules/architecture.md), so the first disqualification test in
/// rules/csharp.md settles it: not module-specific, therefore <c>Utilities/&lt;Subject&gt;/</c>.
/// </para>
/// <para>
/// <b>Never delete-then-write, never overwrite-in-place.</b> Both leave a window in which a
/// concurrent reader observes something that is neither the old nor the new content — an absent
/// file, or a spliced one. A temp file written elsewhere in the same directory and then
/// <c>rename()</c>d over the target has no such window: <c>rename()</c> within one filesystem is a
/// single, atomic directory-entry swap, so a reader always sees either the complete old file or the
/// complete new one.
/// </para>
/// <para>
/// <b>The temp file MUST be created in the same directory as the target.</b> A rename is only atomic
/// within one filesystem, and a different directory can be a different mount. <c>Path.GetTempPath()</c>
/// (<c>/tmp</c> on this panel's hosts) is deliberately never used here for that reason.
/// </para>
/// <para>
/// <b>What this type does NOT do.</b> It takes no lock of its own — a caller writing the same target
/// concurrently from two callers must serialize itself; this type only makes a single write atomic,
/// not a sequence of them. It does not create the target directory; the directory must already exist.
/// </para>
/// </remarks>
public static class AtomicFileWriter
{
    /// <summary>Writes <paramref name="content"/> to <paramref name="targetPath"/> atomically.</summary>
    /// <param name="targetPath">The final path readers observe. Its directory must already exist.</param>
    /// <param name="content">The complete bytes to write; there is no partial-content overload on purpose.</param>
    /// <param name="unixFileMode">
    /// The POSIX permission bits to apply to the temp file before it is renamed into place, so the
    /// live file never appears with looser permissions than intended even for an instant. Ignored on
    /// a platform that does not support <see cref="UnixFileMode"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write and the fsync.</param>
    /// <remarks>
    /// On any failure — including cancellation — the temp file is best-effort deleted and
    /// <paramref name="targetPath"/> is left exactly as it was: this method never partially applies a
    /// write. The temp file's name is derived from <paramref name="targetPath"/> with a random suffix
    /// so two concurrent callers targeting the same file do not collide on the same temp name.
    /// </remarks>
    public static async Task WriteAsync(
        string targetPath,
        byte[] content,
        UnixFileMode unixFileMode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("The target path must name a file inside a directory.", nameof(targetPath));
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(tempPath, unixFileMode);
            }

            // The single atomic step: within one filesystem this is one directory-entry swap. A
            // reader either sees the old file, complete, or the new one, complete — never a mix and
            // never absence.
            AtomicSwap(tempPath, targetPath);
        }
        catch
        {
            TryDeleteOrphan(tempPath);
            throw;
        }
    }

    /// <summary>The real swap: one directory-entry replacement, atomic within a filesystem.</summary>
    /// <param name="tempPath">The finished temp file.</param>
    /// <param name="targetPath">The path it takes the place of.</param>
    private static void AtomicSwap(string tempPath, string targetPath)
    {
        File.Move(tempPath, targetPath, overwrite: true);
    }

    /// <summary>Best-effort removal of an orphaned temp file after a failed attempt.</summary>
    /// <param name="tempPath">The temp file that never made it to <c>rename()</c>.</param>

    private static void TryDeleteOrphan(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Litter, not a hazard (per the threat note's §4): it carries no more than the content
            // this same call was refused to install, and is cleaned up on a later attempt.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
