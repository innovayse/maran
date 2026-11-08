using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Mappers;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="PhpLifecycleMapper"/>: a reading is restated, never decided again.</summary>
public sealed class PhpLifecycleMapperTests
{
    /// <summary>Every field of the reading, and the localized word handed in, arrives on the wire unchanged.</summary>
    [Fact]
    public void Every_field_of_the_reading_and_the_status_word_arrive_unchanged()
    {
        var reading = new PhpLifecycleReading(PhpSupportStatus.SecurityOnly, new DateOnly(2027, 12, 31), 90);

        var mapped = PhpLifecycleMapper.From(reading, "Security fixes only");

        Assert.Equal(PhpSupportStatus.SecurityOnly, mapped.Status);
        Assert.Equal("Security fixes only", mapped.StatusName);
        Assert.Equal(new DateOnly(2027, 12, 31), mapped.EndOfLife);
        Assert.Equal(90, mapped.DaysLeft);
    }

    /// <summary>An unknown reading stays unknown, with no date and no count.</summary>
    [Fact]
    public void An_unknown_reading_stays_unknown_with_no_date_and_no_count()
    {
        var mapped = PhpLifecycleMapper.From(new PhpLifecycleReading(PhpSupportStatus.Unknown, null, null), "Unknown");

        Assert.Equal(PhpSupportStatus.Unknown, mapped.Status);
        Assert.Null(mapped.EndOfLife);
        Assert.Null(mapped.DaysLeft);
    }
}
