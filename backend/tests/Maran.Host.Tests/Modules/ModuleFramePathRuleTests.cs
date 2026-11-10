using Maran.Host.Modules;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleFramePathRule"/>: which files of a screen bundle the panel
/// serves, and as what.
/// </summary>
public sealed class ModuleFramePathRuleTests
{
    /// <summary>Every allowed extension is served with its fixed content type - the accepting control.</summary>
    /// <param name="path">A servable path.</param>
    /// <param name="contentType">The content type the panel assigns it.</param>
    [Theory]
    [InlineData("entry.js", "text/javascript")]
    [InlineData("styles.css", "text/css")]
    [InlineData("messages.json", "application/json")]
    [InlineData("logo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("hero.webp", "image/webp")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("icon.svg", "image/svg+xml")]
    [InlineData("assets/chunk-1.2.js", "text/javascript")]
    [InlineData("a/b/c/d.js", "text/javascript")]
    [InlineData("ENTRY.JS", "text/javascript")]
    public void An_allowed_file_is_served_with_its_fixed_content_type(string path, string contentType)
    {
        Assert.True(ModuleFramePathRule.TryParse(path, out var relative));

        Assert.Equal(path, relative);
        Assert.Equal(contentType, ModuleFramePathRule.ContentTypeOf(relative));
    }

    /// <summary>A document, a script type the panel does not serve, no extension or an unlisted one is refused.</summary>
    /// <param name="path">A path off the allow-list.</param>
    [Theory]
    [InlineData("index.html")]
    [InlineData("page.htm")]
    [InlineData("INDEX.HTML")]
    [InlineData("module.mjs")]
    [InlineData("data.xml")]
    [InlineData("archive.zip")]
    [InlineData("binary.exe")]
    [InlineData("noextension")]
    [InlineData("trailingdot.")]
    [InlineData("entry.js.html")]
    [InlineData("assets/")]
    public void A_file_off_the_extension_allow_list_is_refused(string path)
    {
        Assert.False(ModuleFramePathRule.TryParse(path, out var relative));
        Assert.Equal(string.Empty, relative);
    }

    /// <summary>A path that climbs, is empty in a segment, carries a forbidden character or is not plain is refused.</summary>
    /// <param name="path">A path that is not plain.</param>
    [Theory]
    [InlineData("")]
    [InlineData("../entry.js")]
    [InlineData("a/../entry.js")]
    [InlineData("./entry.js")]
    [InlineData("a//entry.js")]
    [InlineData("/entry.js")]
    [InlineData("a\\entry.js")]
    [InlineData("a b.js")]
    [InlineData("a%2Fb.js")]
    [InlineData("entry.js?x=1")]
    [InlineData("entry.js#x")]
    [InlineData("é.js")]
    [InlineData("a/b/c/d/e.js")]
    public void A_path_that_is_not_plain_is_refused(string path)
    {
        Assert.False(ModuleFramePathRule.TryParse(path, out _));
    }

    /// <summary>The path may be null - the folder URL - and is then not a file.</summary>
    [Fact]
    public void A_missing_path_is_not_a_file()
    {
        Assert.False(ModuleFramePathRule.TryParse(null, out _));
    }

    /// <summary>A segment of exactly the limit is served and one character more is refused.</summary>
    [Fact]
    public void A_segment_is_served_up_to_the_length_limit_and_refused_beyond_it()
    {
        var atLimit = new string('a', ModuleFramePathRule.MaxSegmentLength - 3) + ".js";
        var overLimit = new string('a', ModuleFramePathRule.MaxSegmentLength - 2) + ".js";

        Assert.True(ModuleFramePathRule.TryParse(atLimit, out _));
        Assert.False(ModuleFramePathRule.TryParse(overLimit, out _));
    }

    /// <summary>A path of exactly the segment limit is served and one more segment is refused.</summary>
    [Fact]
    public void A_path_is_served_up_to_the_segment_limit_and_refused_beyond_it()
    {
        Assert.True(ModuleFramePathRule.TryParse("a/b/c/d.js", out _));
        Assert.False(ModuleFramePathRule.TryParse("a/b/c/d/e.js", out _));
    }

    /// <summary>An entry name must be one plain segment with an allowed extension.</summary>
    /// <param name="name">The candidate entry name.</param>
    /// <param name="expected">Whether it is a servable file name.</param>
    [Theory]
    [InlineData("entry.js", true)]
    [InlineData("main-1.0.js", true)]
    [InlineData("dir/entry.js", false)]
    [InlineData("..", false)]
    [InlineData("entry.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void An_entry_name_is_servable_only_as_one_plain_file(string? name, bool expected)
    {
        Assert.Equal(expected, ModuleFramePathRule.IsServableFileName(name));
    }
}
