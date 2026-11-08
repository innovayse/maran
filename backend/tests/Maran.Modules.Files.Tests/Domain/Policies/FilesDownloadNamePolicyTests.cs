using Maran.Modules.Files.Domain.Policies;

namespace Maran.Modules.Files.Tests.Domain.Policies;

/// <summary>Holds <see cref="FilesDownloadNamePolicy"/> to a header-safe, bounded name.</summary>
public sealed class FilesDownloadNamePolicyTests
{
    /// <summary>An ordinary name is kept as it is.</summary>
    [Fact]
    public void An_ordinary_name_is_kept()
    {
        Assert.Equal("report 2026.pdf", FilesDownloadNamePolicy.Sanitise("report 2026.pdf"));
    }

    /// <summary>Control characters, separators and quotes are removed.</summary>
    [Fact]
    public void Control_characters_separators_and_quotes_are_removed()
    {
        Assert.Equal("ab.txt", FilesDownloadNamePolicy.Sanitise("a\r\nb/\\\".txt"));
    }

    /// <summary>A name with nothing usable left falls back to a fixed name.</summary>
    /// <param name="name">A name with nothing usable in it.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("///")]
    [InlineData("\r\n")]
    public void A_name_with_nothing_usable_falls_back(string? name)
    {
        Assert.Equal(FilesDownloadNamePolicy.FallbackName, FilesDownloadNamePolicy.Sanitise(name));
    }

    /// <summary>The length is bounded.</summary>
    [Fact]
    public void A_long_name_is_bounded()
    {
        var cleaned = FilesDownloadNamePolicy.Sanitise(new string('a', 1000));

        Assert.Equal(FilesDownloadNamePolicy.MaximumCharacters, cleaned.Length);
    }
}
