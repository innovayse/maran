using System.Text;
using Maran.SharedKernel.Utilities.IO;

namespace Maran.SharedKernel.Tests.Utilities.IO;

/// <summary>
/// Covers <see cref="AtomicFileWriter"/>'s own guarantee: a reader observes either the complete old
/// file or the complete new one, never a partial mix and never a transient absence.
/// </summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private const UnixFileMode TestFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "atomic-file-writer-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A first write to a target that does not yet exist creates it with the given content.</summary>
    [Fact]
    public async Task Writing_a_new_target_creates_it_with_the_given_content()
    {
        var target = Path.Combine(_directory, "licence.json");

        await AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("{\"v\":1}"), TestFileMode, CancellationToken.None);

        Assert.Equal("{\"v\":1}", await File.ReadAllTextAsync(target));
    }

    /// <summary>A second write replaces the first file's content in full — the ordinary replace path.</summary>
    [Fact]
    public async Task Writing_over_an_existing_target_replaces_its_content_in_full()
    {
        var target = Path.Combine(_directory, "licence.json");
        await AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("old"), TestFileMode, CancellationToken.None);

        await AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("new"), TestFileMode, CancellationToken.None);

        Assert.Equal("new", await File.ReadAllTextAsync(target));
    }

    /// <summary>No stray temp file is left behind after a successful write.</summary>
    [Fact]
    public async Task A_successful_write_leaves_no_temp_file_behind()
    {
        var target = Path.Combine(_directory, "licence.json");

        await AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("content"), TestFileMode, CancellationToken.None);

        var entries = Directory.GetFileSystemEntries(_directory);
        Assert.Single(entries);
        Assert.Equal(target, entries[0]);
    }

    /// <summary>
    /// The previous file survives a write that fails before the atomic rename — the mutant this
    /// slice's proof names for "the atomic rename replaced by a non-atomic write": a non-atomic
    /// overwrite-in-place would have already corrupted or replaced the target by the time the failure
    /// happened; this method's shape (write to a temp file, THEN rename) never touches the target at
    /// all until the write has already fully succeeded.
    /// </summary>
    [Fact]
    public async Task The_previous_file_survives_a_write_cancelled_before_the_rename()
    {
        var target = Path.Combine(_directory, "licence.json");
        await AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("previous"), TestFileMode, CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return AtomicFileWriter.WriteAsync(target, Encoding.UTF8.GetBytes("new, never installed"), TestFileMode, cancelled.Token);
        });

        // The target is exactly what it was before the failed attempt — never absent, never a mix.
        Assert.Equal("previous", await File.ReadAllTextAsync(target));

        // And the failed attempt's temp file does not linger either (best-effort cleanup).
        var entries = Directory.GetFileSystemEntries(_directory);
        Assert.Single(entries);
        Assert.Equal(target, entries[0]);
    }

    /// <summary>A reader watching the target while it is rewritten never sees it absent or partial.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test the atomicity claim had none of, and the reason it took three attempts.</b>
    /// Replacing the rename with the non-atomic <c>File.Delete(target); File.Move(temp, target);</c>
    /// — the form this class exists to avoid — left the whole backend suite green, because nothing
    /// observed the swap at all. A seam was then added so a test could stage a failing swap; that
    /// mutant SURVIVED too, because a test injecting its own swap never runs the real one. The seam
    /// was reverted and this was written instead.
    /// </para>
    /// <para>
    /// <b>Why watching cannot produce a false failure.</b> With one atomic rename the target is
    /// never absent and never half-written — that is an INVARIANT, not a likelihood, so correct code
    /// cannot fail this however the threads interleave. Delete-then-write opens a real window with
    /// no file in it, and a reader spinning across many rewrites falls into it. A test that can only
    /// fail when the property is actually broken is not flaky; it is merely possibly-weak, and the
    /// iteration count is what trades strength against time.
    /// </para>
    /// <para>
    /// UNOBSERVED HERE: a machine killed mid-write. Only the ordering is observed, not power loss.
    /// </para>
    /// </remarks>
    /// <returns>Resolves when the writer and the watcher have both finished.</returns>
    [Fact]
    public async Task A_reader_watching_the_target_never_sees_it_absent_or_partial()
    {
        var target = Path.Combine(_directory, "licence.json");
        var first = Encoding.UTF8.GetBytes("{\"v\":1}");
        var second = Encoding.UTF8.GetBytes("{\"v\":2}");
        await AtomicFileWriter.WriteAsync(target, first, TestFileMode, CancellationToken.None);

        var stop = false;
        var failure = (string?)null;

        var watcher = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                string seen;
                try
                {
                    seen = File.ReadAllText(target);
                }
                catch (FileNotFoundException)
                {
                    // The window delete-then-write opens, and the one an atomic rename cannot.
                    failure = "the target was ABSENT while being rewritten";
                    return;
                }
                catch (IOException)
                {
                    continue;
                }

                if (seen != "{\"v\":1}" && seen != "{\"v\":2}")
                {
                    failure = $"the target was a partial or spliced document: `{seen}`";
                    return;
                }
            }
        });

        for (var round = 0; round < 200 && failure is null; round++)
        {
            await AtomicFileWriter.WriteAsync(
                target,
                round % 2 == 0 ? second : first,
                TestFileMode,
                CancellationToken.None);
        }

        Volatile.Write(ref stop, true);
        await watcher;

        Assert.Null(failure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
