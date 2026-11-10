using System.Net;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The package download: the grant is judged before any request, the bytes are counted and hashed as they
/// arrive, and anything that does not match leaves no file behind. Each refusal is paired with the case it
/// must accept.
/// </summary>
public sealed class PackageDownloaderTests
{
    /// <summary>The package bytes used throughout.</summary>
    private static readonly byte[] Package = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    /// <summary>A grant for the test package, with the given address, digest, size and expiry.</summary>
    /// <param name="world">The test world.</param>
    /// <param name="url">The download address.</param>
    /// <param name="sha256">The declared digest.</param>
    /// <param name="size">The declared size.</param>
    /// <param name="validFor">How long the grant stays valid from now.</param>
    /// <returns>The grant.</returns>
    private static DownloadGrant GrantFor(
        MarketplaceTestWorld world,
        string url,
        string? sha256 = null,
        long? size = null,
        TimeSpan? validFor = null)
    {
        return new DownloadGrant(
            new SensitiveString("grant-secret-id"),
            new SensitiveString(url),
            world.Clock.UtcNow + (validFor ?? TimeSpan.FromMinutes(5)),
            sha256 ?? MarketplaceTestWorld.Sha256Of(Package),
            size ?? Package.Length);
    }

    /// <summary>What the signed catalogue declares for the test package.</summary>
    /// <returns>The declaration.</returns>
    private static ExpectedModulePackage Declared()
    {
        return new ExpectedModulePackage("mail", "1.0.0", MarketplaceTestWorld.Sha256Of(Package), Package.Length);
    }

    /// <summary>The files left in the package directory.</summary>
    /// <param name="world">The test world.</param>
    /// <returns>Their paths; empty when the directory does not exist.</returns>
    private static string[] FilesLeft(MarketplaceTestWorld world)
    {
        return Directory.Exists(world.PackageDirectory) ? Directory.GetFiles(world.PackageDirectory) : [];
    }

    /// <summary>A grant address on another host is refused before any request or file; the same address on the host is accepted.</summary>
    [Fact]
    public async Task A_grant_url_on_another_host_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package); });

        var refused = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://evil.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.GrantHostRefused, refused.Error);
        Assert.Equal(0, world.Handler.Count);
        Assert.False(Directory.Exists(world.PackageDirectory));

        await using var accepted = (await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None)).Value;
        Assert.Equal(1, world.Handler.Count);
    }

    /// <summary>A grant address on the right host over plain http, or with user information, is refused.</summary>
    [Theory]
    [InlineData("http://marketplace.test/d/1")]
    [InlineData("https://user:pass@marketplace.test/d/1")]
    [InlineData("https://marketplace.test:8443/d/1")]
    [InlineData("not a url")]
    public async Task A_grant_url_that_is_not_exactly_the_configured_host_is_refused(string url)
    {
        using var world = new MarketplaceTestWorld();

        var result = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, url), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.GrantHostRefused, result.Error);
        Assert.Equal(0, world.Handler.Count);
    }

    /// <summary>A host that sends more than the declared size is cut off and refused; the exact size is accepted.</summary>
    [Fact]
    public async Task A_download_longer_than_declared_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        var longer = Package.Append((byte)1).ToArray();
        world.Handler.Reply(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableMemoryStream(longer)),
            };
        });

        var refused = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadSizeMismatch, refused.Error);
        Assert.Empty(FilesLeft(world));

        world.Handler.Reply(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableMemoryStream(Package)),
            };
        });

        await using var accepted = (await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None)).Value;
        Assert.Single(FilesLeft(world));
    }

    /// <summary>A host that sends less than the declared size is refused and leaves no file.</summary>
    [Fact]
    public async Task A_download_shorter_than_declared_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableMemoryStream(Package[..5])),
            };
        });

        var result = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadSizeMismatch, result.Error);
        Assert.Empty(FilesLeft(world));
    }

    /// <summary>A declared length that disagrees with the grant is refused before a byte is stored.</summary>
    [Fact]
    public async Task A_declared_content_length_that_disagrees_with_the_grant_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package.Append((byte)1).ToArray()); });

        var result = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadSizeMismatch, result.Error);
        Assert.Empty(FilesLeft(world));
    }

    /// <summary>Bytes whose digest is not the declared one are refused and deleted; the matching bytes are accepted.</summary>
    [Fact]
    public async Task A_digest_mismatch_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        var swapped = (byte[])Package.Clone();
        swapped[0] ^= 0xFF;
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, swapped); });

        var refused = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadDigestMismatch, refused.Error);
        Assert.Empty(FilesLeft(world));

        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package); });
        await using var accepted = (await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None)).Value;

        await using var stream = await accepted.OpenAsync(CancellationToken.None);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(Package, copy.ToArray());
    }

    /// <summary>A grant that disagrees with the catalogue on digest or size is refused before any request; an agreeing one is not.</summary>
    [Fact]
    public async Task A_grant_that_disagrees_with_the_catalogue_is_refused_before_any_request()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package); });
        var url = "https://marketplace.test/d/1";
        var otherDigest = MarketplaceTestWorld.Sha256Of([1]);

        var digest = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, url, sha256: otherDigest), Declared(), CancellationToken.None);
        var size = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, url, size: Package.Length + 1), Declared(), CancellationToken.None);
        var malformed = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, url, sha256: "zz"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadDigestMismatch, digest.Error);
        Assert.Equal(MarketplaceClientRefusals.DownloadSizeMismatch, size.Error);
        Assert.Equal(MarketplaceClientRefusals.DownloadDigestMismatch, malformed.Error);
        Assert.Equal(0, world.Handler.Count);

        await using var agreeing = (await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, url), Declared(), CancellationToken.None)).Value;
        Assert.Equal(1, world.Handler.Count);
    }

    /// <summary>A package larger than the configured ceiling is refused before any request.</summary>
    [Fact]
    public async Task A_package_over_the_configured_ceiling_is_refused_before_any_request()
    {
        using var world = new MarketplaceTestWorld();
        world.Options.MaxPackageBytes = Package.Length - 1;

        var result = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.DownloadSizeMismatch, result.Error);
        Assert.Equal(0, world.Handler.Count);
    }

    /// <summary>An expired grant is refused before any request.</summary>
    [Fact]
    public async Task An_expired_grant_is_refused_before_any_request()
    {
        using var world = new MarketplaceTestWorld();

        var result = await world.Downloader.DownloadAsync(
            world.Transport,
            GrantFor(world, "https://marketplace.test/d/1", validFor: TimeSpan.FromSeconds(-1)),
            Declared(),
            CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ResponseInvalid, result.Error);
        Assert.Equal(0, world.Handler.Count);
    }

    /// <summary>A connection that drops half way leaves no file behind.</summary>
    [Fact]
    public async Task A_connection_dropped_mid_download_leaves_no_file()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream(Package[..4])) };
        });

        var result = await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Unreachable, result.Error);
        Assert.Empty(FilesLeft(world));
    }

    /// <summary>The file is created readable and writable by its owner only, under a name the host did not choose.</summary>
    [Fact]
    public async Task The_downloaded_file_is_owner_only()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package); });

        await using var source = (await world.Downloader.DownloadAsync(
            world.Transport, GrantFor(world, "https://marketplace.test/d/1"), Declared(), CancellationToken.None)).Value;

        var file = Assert.Single(FilesLeft(world));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }

        Assert.DoesNotContain("grant", Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mail", source.Expected.ModuleId);
    }
}
