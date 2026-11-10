using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="FileLeaseWriter"/>: the lease lands in one atomic step, replacing the old file whole, with the
/// licence file's mode, and leaves no temporary file behind.
/// </summary>
public sealed class FileLeaseWriterTests : IDisposable
{
    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "lease-writer-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The lease file, in a directory that does not exist yet.</summary>
    private string LeasePath
    {
        get { return Path.Combine(_directory, "state", "lease.json"); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds the writer over the test path.</summary>
    /// <returns>The writer.</returns>
    private FileLeaseWriter Writer()
    {
        return new FileLeaseWriter(
            new Microsoft.Extensions.Options.OptionsWrapper<LeaseStorageOptions>(
                new LeaseStorageOptions { LeaseFilePath = LeasePath }));
    }

    /// <summary>The text arrives whole in a directory that had to be created, and nothing else is left in it.</summary>
    [Fact]
    public async Task The_text_is_written_whole_and_no_temporary_file_is_left()
    {
        await Writer().WriteAsync("{\"payload\":{},\"signature\":\"x\"}", CancellationToken.None);

        Assert.Equal("{\"payload\":{},\"signature\":\"x\"}", await File.ReadAllTextAsync(LeasePath));
        Assert.Equal([LeasePath], Directory.GetFileSystemEntries(Path.GetDirectoryName(LeasePath)!));
    }

    /// <summary>A second write replaces the first completely.</summary>
    [Fact]
    public async Task A_second_write_replaces_the_first_whole()
    {
        await Writer().WriteAsync("first lease text that is the longer of the two", CancellationToken.None);
        await Writer().WriteAsync("second", CancellationToken.None);

        Assert.Equal("second", await File.ReadAllTextAsync(LeasePath));
    }

    /// <summary>The file is readable by its owner and group and by nobody else, like the licence beside it.</summary>
    [Fact]
    public async Task The_file_is_private_to_its_owner_and_group()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await Writer().WriteAsync("lease", CancellationToken.None);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            File.GetUnixFileMode(LeasePath));
    }

    /// <summary>Blank text is refused and writes nothing, so a lease can never be replaced by an empty file.</summary>
    [Fact]
    public async Task Blank_text_is_refused_and_nothing_is_written()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => { return Writer().WriteAsync("  ", CancellationToken.None); });

        Assert.False(File.Exists(LeasePath));
    }
}
