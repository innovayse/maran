using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="PhpExtensionCatalogue"/>, the panel's copy of the agent's closed list.</summary>
public sealed class PhpExtensionCatalogueTests
{
    /// <summary>The thirteen names the agent's catalogue holds, written out as the independent expectation.</summary>
    private static readonly string[] AgentCatalogue =
    [
        "bcmath", "curl", "gd", "gmp", "imagick", "intl", "mbstring", "mysql", "opcache", "redis", "soap", "xml", "zip",
    ];

    /// <summary>The panel's copy is exactly the agent's thirteen names, alphabetical.</summary>
    [Fact]
    public void The_panels_copy_is_exactly_the_agents_thirteen_names_in_alphabetical_order()
    {
        Assert.Equal(AgentCatalogue, PhpExtensionCatalogue.Names);
    }

    /// <summary>Every catalogue name is known.</summary>
    [Fact]
    public void Every_catalogue_name_is_known()
    {
        Assert.All(AgentCatalogue, name =>
        {
            Assert.True(PhpExtensionCatalogue.IsKnown(name), name);
        });
    }

    /// <summary>A name outside the catalogue, in any spelling that is not exact, is not known.</summary>
    /// <param name="name">A candidate that is not a catalogue name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("GD")]
    [InlineData("gd ")]
    [InlineData("php8.3-gd")]
    [InlineData("mysqli")]
    [InlineData("Zend OPcache")]
    [InlineData("gd\n")]
    [InlineData("../gd")]
    [InlineData("xdebug")]
    public void A_name_outside_the_catalogue_is_not_known(string name)
    {
        Assert.False(PhpExtensionCatalogue.IsKnown(name));
    }

    /// <summary>No name at all is not known.</summary>
    [Fact]
    public void A_missing_name_is_not_known()
    {
        Assert.False(PhpExtensionCatalogue.IsKnown(null));
    }
}
