using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="PhpSettingSpelling"/>.</summary>
public sealed class PhpSettingSpellingTests
{
    /// <summary>A size is written to php.ini with the mebibyte suffix and read back without it.</summary>
    [Fact]
    public void A_size_is_written_with_the_mebibyte_suffix_and_read_back_without_it()
    {
        Assert.Equal("256M", PhpSettingSpelling.ToIni(PhpSettingKind.Size, "256"));
        Assert.Equal("256", PhpSettingSpelling.ToWire(PhpSettingKind.Size, "256M"));
    }

    /// <summary>Seconds, counts and timezones are written as they are sent.</summary>
    [Theory]
    [InlineData(PhpSettingKind.Seconds, "120")]
    [InlineData(PhpSettingKind.Count, "3000")]
    [InlineData(PhpSettingKind.Timezone, "Europe/Berlin")]
    public void Seconds_counts_and_timezones_are_written_as_they_are_sent(PhpSettingKind kind, string value)
    {
        Assert.Equal(value, PhpSettingSpelling.ToIni(kind, value));
        Assert.Equal(value, PhpSettingSpelling.ToWire(kind, value));
    }

    /// <summary>Only whole numbers of ASCII digits are read as numbers.</summary>
    [Theory]
    [InlineData("0", true)]
    [InlineData("256", true)]
    [InlineData("999999999", true)]
    [InlineData("", false)]
    [InlineData("-1", false)]
    [InlineData("+5", false)]
    [InlineData("12M", false)]
    [InlineData("1 2", false)]
    [InlineData("1000000000", false)]
    [InlineData("٣", false)]
    public void Only_whole_numbers_of_ascii_digits_are_read_as_numbers(string text, bool expected)
    {
        Assert.Equal(expected, PhpSettingSpelling.TryReadNumber(text, out _));
    }

    /// <summary>A timezone is accepted by shape: one to three components of the allowed characters.</summary>
    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Europe/Berlin", true)]
    [InlineData("America/Indiana/Knox", true)]
    [InlineData("Etc/GMT+5", true)]
    [InlineData("", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("/Europe/Berlin", false)]
    [InlineData("Europe//Berlin", false)]
    [InlineData("A/B/C/D", false)]
    [InlineData("Europe/Berlin\nphp_admin_value[x] = y", false)]
    [InlineData("Europe/Berlin;", false)]
    public void A_timezone_is_accepted_by_shape(string value, bool expected)
    {
        Assert.Equal(expected, PhpSettingSpelling.IsTimezoneShape(value));
    }
}
