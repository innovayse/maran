using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>A downloaded package is read while it lives and deleted when it is disposed.</summary>
public sealed class DownloadedPackageSourceTests
{
    /// <summary>What the catalogue declared, irrelevant to the file handling under test.</summary>
    private static readonly ExpectedModulePackage Declared = new("mail", "1.0.0", new string('a', 64), 3);

    /// <summary>The package can be read while the source lives, and disposing deletes the file.</summary>
    [Fact]
    public async Task Disposing_the_source_deletes_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maran-source-{Guid.NewGuid():N}.ipm");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var source = new DownloadedPackageSource(path, Declared, "marketplace test");

        await using (var stream = await source.OpenAsync(CancellationToken.None))
        {
            Assert.Equal(1, stream.ReadByte());
        }

        Assert.True(File.Exists(path));
        await source.DisposeAsync();

        Assert.False(File.Exists(path));
    }

    /// <summary>Disposing twice is harmless, and a disposed source refuses to open.</summary>
    [Fact]
    public async Task A_disposed_source_refuses_to_open_and_disposing_again_is_harmless()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maran-source-{Guid.NewGuid():N}.ipm");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var source = new DownloadedPackageSource(path, Declared, "marketplace test");

        await source.DisposeAsync();
        await source.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => { return source.OpenAsync(CancellationToken.None); });
    }

    /// <summary>The source carries what the catalogue promised and where the bytes came from.</summary>
    [Fact]
    public async Task The_source_carries_the_catalogues_promise_and_its_origin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maran-source-{Guid.NewGuid():N}.ipm");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await using var source = new DownloadedPackageSource(path, Declared, "marketplace test");

        Assert.Equal(Declared, source.Expected);
        Assert.Equal("marketplace test", source.Origin);
    }
}
