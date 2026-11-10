using Maran.Modules.Marketplace.Domain.Entities;

namespace Maran.Modules.Marketplace.Tests.Domain.Entities;

/// <summary>The highest version seen per module only ever rises.</summary>
public sealed class ModuleVersionFloorTests
{
    /// <summary>The instant the floor is created.</summary>
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A higher version raises the floor and records when.</summary>
    [Fact]
    public void A_higher_version_raises_the_floor()
    {
        var floor = new ModuleVersionFloor("mail", "1.2.0", At);

        var raised = floor.Raise("1.10.0", At.AddMinutes(1));

        Assert.True(raised);
        Assert.Equal("1.10.0", floor.HighestVersionSeen);
        Assert.Equal(At.AddMinutes(1), floor.UpdatedAt);
    }

    /// <summary>A lower version never lowers the floor, which is the whole point of it.</summary>
    [Fact]
    public void A_lower_version_never_lowers_the_floor()
    {
        var floor = new ModuleVersionFloor("mail", "1.2.0", At);

        var raised = floor.Raise("1.1.9", At.AddMinutes(1));

        Assert.False(raised);
        Assert.Equal("1.2.0", floor.HighestVersionSeen);
        Assert.Equal(At, floor.UpdatedAt);
    }

    /// <summary>The same version changes nothing, and a pre-release ranks below its release.</summary>
    [Fact]
    public void The_same_version_changes_nothing_and_a_pre_release_ranks_below_its_release()
    {
        var floor = new ModuleVersionFloor("mail", "1.2.0", At);

        Assert.False(floor.Raise("1.2.0", At.AddMinutes(1)));
        Assert.False(floor.Raise("1.2.0-beta.1", At.AddMinutes(1)));
        Assert.Equal("1.2.0", floor.HighestVersionSeen);
    }

    /// <summary>Versions compare as numbers, so 1.10.0 is above 1.9.0 and not below it as text would have it.</summary>
    [Fact]
    public void Versions_compare_as_numbers_and_not_as_text()
    {
        var floor = new ModuleVersionFloor("mail", "1.9.0", At);

        Assert.True(floor.Raise("1.10.0", At.AddMinutes(1)));
    }

    /// <summary>A version that is not a semantic version moves nothing.</summary>
    [Fact]
    public void A_version_that_is_not_a_semantic_version_moves_nothing()
    {
        var floor = new ModuleVersionFloor("mail", "1.2.0", At);

        Assert.False(floor.Raise("latest", At.AddMinutes(1)));
        Assert.False(floor.Raise("https://example.test/9.9.9", At.AddMinutes(1)));
        Assert.Equal("1.2.0", floor.HighestVersionSeen);
    }
}
