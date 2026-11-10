using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;

namespace Maran.Modules.Apps.Tests.Domain.ValueObjects;

/// <summary>The closed catalogue: an id exists only as one of its members, in exactly its spelling.</summary>
public sealed class AppIdTests
{
    /// <summary>Every member of the catalogue parses, with its family, kind and version.</summary>
    /// <param name="text">The catalogue spelling.</param>
    /// <param name="family">The expected family.</param>
    /// <param name="version">The expected version.</param>
    [Theory]
    [InlineData("nginx", AppFamily.Nginx, "")]
    [InlineData("apache", AppFamily.Apache, "")]
    [InlineData("php-7.4", AppFamily.Php, "7.4")]
    [InlineData("php-8.4", AppFamily.Php, "8.4")]
    [InlineData("nodejs-18", AppFamily.Node, "18")]
    [InlineData("nodejs-24", AppFamily.Node, "24")]
    public void A_member_of_the_catalogue_parses(string text, AppFamily family, string version)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        Assert.Equal(family, appId.Family);
        Assert.Equal(version, appId.Version);
        Assert.Equal(text, appId.Value);
    }

    /// <summary>A web server is a web server and a runtime is a runtime.</summary>
    [Fact]
    public void The_kind_follows_the_family()
    {
        Assert.True(AppId.TryParse("apache", out var apache));
        Assert.True(AppId.TryParse("php-8.3", out var php));

        Assert.Equal(AppKind.WebServer, apache.Kind);
        Assert.Equal(AppKind.Runtime, php.Kind);
    }

    /// <summary>
    /// Anything that is not exactly a member is refused: a package name, a version outside the catalogue,
    /// another case, a trailing space, a path and a control character.
    /// </summary>
    /// <param name="text">The candidate text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apache2")]
    [InlineData("php8.3")]
    [InlineData("php-")]
    [InlineData("php-5.6")]
    [InlineData("php-8.3 ")]
    [InlineData("PHP-8.3")]
    [InlineData("Nginx")]
    [InlineData("nodejs-17")]
    [InlineData("nodejs-22.1")]
    [InlineData("../nginx")]
    [InlineData("nginx\n")]
    [InlineData("php-8.3\0")]
    public void Anything_outside_the_catalogue_is_refused(string? text)
    {
        Assert.False(AppId.TryParse(text, out _));
    }

    /// <summary>A version outside the catalogue yields no id when built from parts.</summary>
    [Fact]
    public void A_runtime_version_outside_the_catalogue_has_no_id()
    {
        Assert.Null(AppId.Of(AppFamily.Php, "5.6"));
        Assert.NotNull(AppId.Of(AppFamily.Node, "22"));
    }
}
