using Maran.Host.Modules;

namespace Maran.Host.Tests.Modules;

/// <summary>Behavioral contract of <see cref="PanelVersion"/>, the Sdk's window onto the panel's own version.</summary>
public sealed class PanelVersionTests
{
    /// <summary>The version is stated, and carries no source revision that would change with every commit.</summary>
    [Fact]
    public void The_version_is_stated_without_a_source_revision()
    {
        var current = new PanelVersion().Current;

        Assert.False(string.IsNullOrWhiteSpace(current));
        Assert.DoesNotContain('+', current);
    }
}
