using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Maran.Packaging.Models;
using Maran.Packaging.Options;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;
using Maran.SharedKernel.Results;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// The reader against hostile archives. Every refusal has the good package beside it passing through
/// the same reader with the same limits, so a reader changed to refuse everything cannot pass.
/// </summary>
public sealed class IpmReaderTests : IDisposable
{
    /// <summary>The throwaway key the packages of this run are signed with.</summary>
    private readonly ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>A good package is read, its manifest and signature set apart from the payload.</summary>
    [Fact]
    public void A_good_package_is_read_with_manifest_and_signature_apart_from_the_payload()
    {
        var result = Read(new IpmFixtureBuilder(_key).Build());

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.NotEmpty(result.Value.ManifestBytes);
        Assert.NotEmpty(result.Value.SignatureBytes);
        Assert.Equal(3, result.Value.Payload.Count);
        Assert.False(result.Value.Payload.ContainsKey("ipm.json"));
        Assert.False(result.Value.Payload.ContainsKey("ipm.sig"));
    }

    /// <summary>A package with no bytes is refused as empty.</summary>
    [Fact]
    public void An_empty_stream_is_refused_as_empty_input()
    {
        RefusalAssert.Is("IpmEmptyInput", Read([]).Error);
    }

    /// <summary>An archive with no entries is refused as empty.</summary>
    [Fact]
    public void An_archive_with_no_entries_is_refused_as_empty_input()
    {
        using var memory = new MemoryStream();
        using (new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        RefusalAssert.Is("IpmEmptyInput", Read(memory.ToArray()).Error);
    }

    /// <summary>Bytes that are not an archive are refused as malformed, not thrown at the caller.</summary>
    [Fact]
    public void Bytes_that_are_not_an_archive_are_refused_as_malformed()
    {
        RefusalAssert.Is("IpmArchiveMalformed", Read(Encoding.UTF8.GetBytes(new string('x', 200))).Error);
    }

    /// <summary>A name leaving the package folder is refused as a traversal.</summary>
    /// <param name="name">The planted entry name.</param>
    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("/etc/cron.d/evil")]
    [InlineData("lib\\..\\evil.dll")]
    public void An_entry_that_leaves_the_package_folder_is_refused(string name)
    {
        var result = Read(new IpmFixtureBuilder(_key).WithRawEntry(name, [1, 2, 3]).Build());

        RefusalAssert.Is("IpmEntryTraversal", result.Error);
    }

    /// <summary>An entry stored as a symbolic link is refused, and a plain file with a Unix mode is accepted beside it.</summary>
    [Fact]
    public void A_symbolic_link_entry_is_refused_and_a_regular_file_mode_is_accepted()
    {
        var symbolicLink = unchecked((int)0xA1FF0000);
        var regularFile = unchecked((int)0x81A40000);

        var refused = Read(new IpmFixtureBuilder(_key).WithRawEntry("lib/link.dll", [1], symbolicLink).Build());
        var accepted = Read(new IpmFixtureBuilder(_key).WithRawEntry("lib/plain.dll", [1], regularFile).Build());

        RefusalAssert.Is("IpmEntryNotRegular", refused.Error);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
    }

    /// <summary>A directory entry is refused, because a package holds files only.</summary>
    [Fact]
    public void A_directory_entry_is_refused()
    {
        var result = Read(new IpmFixtureBuilder(_key).WithRawEntry("lib/", []).Build());

        RefusalAssert.Is("IpmEntryNotRegular", result.Error);
    }

    /// <summary>A name that is not valid UTF-8 is refused instead of being guessed at.</summary>
    [Fact]
    public void An_entry_name_that_is_not_valid_utf8_is_refused()
    {
        var package = new IpmFixtureBuilder(_key)
            .WithNameEncoding(Encoding.Latin1)
            .WithRawEntry("lib/café.dll", [1])
            .Build();

        RefusalAssert.Is("IpmEntryNameInvalid", Read(package).Error);
    }

    /// <summary>Two entries with one name are refused, and so are two that differ only in case.</summary>
    /// <param name="duplicate">The second entry's name.</param>
    [Theory]
    [InlineData("lib/Hello.dll")]
    [InlineData("lib/HELLO.dll")]
    [InlineData("IPM.JSON")]
    public void A_duplicate_or_case_colliding_entry_is_refused(string duplicate)
    {
        var result = Read(new IpmFixtureBuilder(_key).WithRawEntry(duplicate, [9]).Build());

        RefusalAssert.Is("IpmDuplicateEntry", result.Error);
    }

    /// <summary>An entry larger than the cap is refused and one under it is read.</summary>
    [Fact]
    public void An_entry_over_the_size_cap_is_refused_and_one_under_it_is_read()
    {
        var limits = IpmLimits.Default with { MaxEntryBytes = 1024 };

        var refused = Read(new IpmFixtureBuilder(_key).WithPayload("lib/big.dll", Noise(2048)).Build(), limits);
        var accepted = Read(new IpmFixtureBuilder(_key).WithPayload("lib/small.dll", Noise(512)).Build(), limits);

        RefusalAssert.Is("IpmEntryTooLarge", refused.Error);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
    }

    /// <summary>An entry that compresses by more than the ratio is a bomb and is refused; the same size of incompressible data is read.</summary>
    [Fact]
    public void A_compression_bomb_is_refused_and_incompressible_data_of_the_same_size_is_read()
    {
        var limits = IpmLimits.Default with { RatioFloorBytes = 4096 };

        var bomb = Read(new IpmFixtureBuilder(_key).WithPayload("lib/zeros.dll", new byte[1024 * 1024]).Build(), limits);
        var honest = Read(new IpmFixtureBuilder(_key).WithPayload("lib/noise.dll", Noise(1024 * 1024)).Build(), limits);

        RefusalAssert.Is("IpmRatioExceeded", bomb.Error);
        Assert.True(honest.IsSuccess, honest.Error?.Code);
    }

    /// <summary>The ratio is not judged below its floor, so a tiny entry of zeros is read.</summary>
    [Fact]
    public void A_tiny_highly_compressible_entry_below_the_ratio_floor_is_read()
    {
        var result = Read(new IpmFixtureBuilder(_key).WithPayload("lib/zeros.dll", new byte[8192]).Build());

        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    /// <summary>More entries than the cap are refused; exactly the cap is read.</summary>
    [Fact]
    public void More_entries_than_the_cap_are_refused_and_exactly_the_cap_is_read()
    {
        var package = new IpmFixtureBuilder(_key).Build();

        RefusalAssert.Is("IpmTooManyEntries", Read(package, IpmLimits.Default with { MaxEntries = 4 }).Error);
        Assert.True(Read(package, IpmLimits.Default with { MaxEntries = 5 }).IsSuccess);
    }

    /// <summary>A package larger than the total cap is refused before it is opened.</summary>
    [Fact]
    public void A_package_larger_than_the_total_cap_is_refused()
    {
        var package = new IpmFixtureBuilder(_key).Build();

        RefusalAssert.Is("IpmPackageTooLarge", Read(package, IpmLimits.Default with { MaxTotalBytes = 200 }).Error);
        Assert.True(Read(package).IsSuccess);
    }

    /// <summary>An archive with no manifest entry is refused, naming the manifest.</summary>
    [Fact]
    public void An_archive_without_a_manifest_is_refused()
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("lib/Hello.dll").Open();
            stream.WriteByte(1);
        }

        RefusalAssert.Is("IpmManifestMissing", Read(memory.ToArray()).Error);
    }

    /// <summary>A package with no signature entry is refused, naming the signature.</summary>
    [Fact]
    public void A_package_without_a_signature_entry_is_refused()
    {
        var result = Read(new IpmFixtureBuilder(_key).WithoutSignature().Build());

        RefusalAssert.Is("IpmSignatureMissing", result.Error);
    }

    /// <summary>A null stream is a caller bug and throws; it is not a refusal of a package.</summary>
    [Fact]
    public void A_null_stream_is_a_caller_bug()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return new IpmReader(IpmLimits.Default).Read(null!);
        });
    }

    /// <summary>Reads package bytes with a reader holding the given caps.</summary>
    /// <param name="package">The package bytes.</param>
    /// <param name="limits">The caps; the defaults when null.</param>
    /// <returns>The reader's answer.</returns>
    private static Result<IpmArchive> Read(
        byte[] package,
        IpmLimits? limits = null)
    {
        using var stream = new MemoryStream(package);
        return new IpmReader(limits ?? IpmLimits.Default).Read(stream);
    }

    /// <summary>Deterministic incompressible bytes.</summary>
    /// <param name="length">How many.</param>
    /// <returns>Pseudo-random content that deflate cannot shrink.</returns>
    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(7).NextBytes(bytes);
        return bytes;
    }
}
