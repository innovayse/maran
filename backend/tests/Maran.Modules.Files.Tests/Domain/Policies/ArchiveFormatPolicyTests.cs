using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Tests.Domain.Policies;

/// <summary>Holds <see cref="ArchiveFormatPolicy"/> to reading a format from a name and writing a suffix.</summary>
public sealed class ArchiveFormatPolicyTests
{
    /// <summary>The known suffixes are recognised, case-insensitively.</summary>
    /// <param name="name">An archive name.</param>
    /// <param name="expected">The format it stands for.</param>
    [Theory]
    [InlineData("site.zip", ArchiveFormatKind.Zip)]
    [InlineData("SITE.ZIP", ArchiveFormatKind.Zip)]
    [InlineData("site.tar.gz", ArchiveFormatKind.TarGz)]
    [InlineData("site.tgz", ArchiveFormatKind.TarGz)]
    public void A_known_suffix_names_its_format(string name, ArchiveFormatKind expected)
    {
        Assert.True(ArchiveFormatPolicy.TryFromName(name, out var format));
        Assert.Equal(expected, format);
    }

    /// <summary>An unknown suffix is not an archive the manager extracts.</summary>
    [Fact]
    public void An_unknown_suffix_is_not_recognised()
    {
        Assert.False(ArchiveFormatPolicy.TryFromName("site.rar", out _));
    }

    /// <summary>Each format writes its own suffix.</summary>
    [Fact]
    public void Each_format_writes_its_own_suffix()
    {
        Assert.Equal(".zip", ArchiveFormatPolicy.SuffixOf(ArchiveFormatKind.Zip));
        Assert.Equal(".tar.gz", ArchiveFormatPolicy.SuffixOf(ArchiveFormatKind.TarGz));
    }
}
