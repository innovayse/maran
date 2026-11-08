using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>The rules for a zone's name, for collisions between accounts' zones, and for the serial.</summary>
public sealed class ZonePoliciesTests
{
    /// <summary>A host name of two or more labels is a valid apex, spelled lower case with no dot.</summary>
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData(" Example.COM. ", "example.com")]
    [InlineData("a.b.example.co.uk", "a.b.example.co.uk")]
    public void A_host_name_is_a_valid_apex(string typed, string stored)
    {
        var normalized = ZoneNamePolicy.Normalize(typed);

        Assert.Equal(stored, normalized);
        Assert.Equal(ZoneNameVerdict.Accepted, ZoneNamePolicy.Check(normalized));
    }

    /// <summary>A single label, an address, an underscore, a slash and an over-long name are refused.</summary>
    [Theory]
    [InlineData("com")]
    [InlineData("1.2.3.4")]
    [InlineData("ex_ample.com")]
    [InlineData("a/b.com")]
    [InlineData("a..com")]
    [InlineData("")]
    public void A_name_that_is_not_an_apex_is_refused(string typed)
    {
        Assert.Equal(ZoneNameVerdict.Invalid, ZoneNamePolicy.Check(ZoneNamePolicy.Normalize(typed)));
    }

    /// <summary>A name longer than 253 characters is refused.</summary>
    [Fact]
    public void A_name_longer_than_the_limit_is_refused()
    {
        var name = string.Join('.', Enumerable.Repeat(new string('a', 50), 6)) + ".com";

        Assert.Equal(ZoneNameVerdict.Invalid, ZoneNamePolicy.Check(name));
    }

    /// <summary>The same name, a parent and a child of another account's zone are each refused by name.</summary>
    [Fact]
    public void A_collision_with_a_foreign_zone_is_named()
    {
        string[] foreign = ["example.com"];

        Assert.Equal(ZoneOverlapVerdict.SameAsForeignZone, ZoneOverlapPolicy.Check("example.com", foreign));
        Assert.Equal(ZoneOverlapVerdict.ChildOfForeignZone, ZoneOverlapPolicy.Check("sub.example.com", foreign));
        Assert.Equal(ZoneOverlapVerdict.ParentOfForeignZone, ZoneOverlapPolicy.Check("com", ["example.com"]));
        Assert.Equal(ZoneOverlapVerdict.ParentOfForeignZone, ZoneOverlapPolicy.Check("example.com", ["a.example.com"]));
    }

    /// <summary>A name that merely ends with the same letters is not a child: the boundary is a dot.</summary>
    [Fact]
    public void A_lookalike_suffix_is_not_a_collision()
    {
        Assert.Equal(ZoneOverlapVerdict.Clear, ZoneOverlapPolicy.Check("myexample.com", ["example.com"]));
        Assert.Equal(ZoneOverlapVerdict.Clear, ZoneOverlapPolicy.Check("example.com", ["myexample.com"]));
        Assert.Equal(ZoneOverlapVerdict.Clear, ZoneOverlapPolicy.Check("example.org", ["example.com"]));
        Assert.Equal(ZoneOverlapVerdict.Clear, ZoneOverlapPolicy.Check("example.com", []));
    }

    /// <summary>The first serial of a day is the date followed by 00, and it only ever increases.</summary>
    [Fact]
    public void The_serial_is_the_date_and_increases()
    {
        var day = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

        var first = ZoneSerialPolicy.Next(0, day);
        var second = ZoneSerialPolicy.Next(first, day);
        var next = ZoneSerialPolicy.Next(second, day.AddDays(1));

        Assert.Equal(2026110700, first);
        Assert.Equal(2026110701, second);
        Assert.Equal(2026110800, next);
    }

    /// <summary>A serial ahead of today's date is never lowered, so a clock that went backwards cannot rewind a zone.</summary>
    [Fact]
    public void A_serial_ahead_of_today_is_never_lowered()
    {
        var day = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal(2026110951, ZoneSerialPolicy.Next(2026110950, day));
    }
}
