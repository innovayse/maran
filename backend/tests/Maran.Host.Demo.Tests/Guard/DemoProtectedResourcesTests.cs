using Maran.Host.Demo.Guard;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The protected set says what it holds and whether it was ever filled from a seeded database.</summary>
public sealed class DemoProtectedResourcesTests
{
    /// <summary>A new set is empty and not loaded.</summary>
    [Fact]
    public void A_new_set_is_empty_and_not_loaded()
    {
        var set = new DemoProtectedResources();

        Assert.Equal(0, set.Count);
        Assert.False(set.IsLoaded);
    }

    /// <summary>Protecting an id does not count as loading the set: only the loader's say-so does.</summary>
    [Fact]
    public void Protecting_an_id_does_not_mark_the_set_loaded()
    {
        var set = new DemoProtectedResources();
        var id = Guid.NewGuid();

        set.Protect(id);

        Assert.True(set.IsProtected(id));
        Assert.False(set.IsLoaded);
    }

    /// <summary>Marking the set loaded is remembered.</summary>
    [Fact]
    public void Marking_the_set_loaded_is_remembered()
    {
        var set = new DemoProtectedResources();

        set.MarkLoaded();

        Assert.True(set.IsLoaded);
    }
}
