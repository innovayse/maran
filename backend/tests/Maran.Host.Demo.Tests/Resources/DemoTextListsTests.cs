using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Resources;

namespace Maran.Host.Demo.Tests.Resources;

/// <summary>The content filter's lists are embedded: one blocked-term file per language, none empty.</summary>
public sealed class DemoTextListsTests
{
    /// <summary>The languages the panel ships; each owns a blocked-term file.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The documentation namespaces the allow-list file names besides the allowed hosts.</summary>
    private static readonly string[] ExpectedDocumentationHosts = ["w3.org", "apache.org", "schema.org"];

    /// <summary>The positive control for the vacuity guard: every language's file is embedded and has terms.</summary>
    /// <param name="culture">The language.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_language_has_a_blocked_term_file_with_terms(string culture)
    {
        var resource = DemoTextLists.BlockedTermsPrefix + culture + ".txt";

        Assert.Contains(resource, DemoTextLists.BlockedTermResources());
        using var stream = typeof(DemoTextLists).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var entries = reader.ReadToEnd()
            .Split('\n')
            .Where(line => { return line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'); });
        Assert.NotEmpty(entries);
    }

    /// <summary>The files are read together: the list holds terms from each language.</summary>
    [Fact]
    public void The_blocked_terms_join_every_languages_file()
    {
        var terms = DemoTextLists.BlockedTerms();

        Assert.Contains("casino*", terms);
        Assert.Contains("казино*", terms);
        Assert.Contains("կազինո*", terms);
        Assert.DoesNotContain(terms, term => { return term.StartsWith('#') || term.Trim().Length == 0; });
    }

    /// <summary>The allow-list names the documentation hosts only; loopback is not on it, and no comment line is.</summary>
    [Fact]
    public void The_allowed_hosts_are_the_documentation_names_and_not_loopback()
    {
        var hosts = DemoTextLists.AllowedHosts();

        Assert.Contains("example.com", hosts);
        Assert.Contains("test", hosts);
        Assert.DoesNotContain("localhost", hosts);
        Assert.DoesNotContain("127.0.0.1", hosts);
        Assert.DoesNotContain(hosts, host => { return host.StartsWith('#'); });
    }

    /// <summary>The documentation namespaces are their own embedded list, apart from the allow-list.</summary>
    [Fact]
    public void The_documentation_hosts_are_a_separate_curated_list()
    {
        var documentation = DemoTextLists.DocumentationHosts();

        Assert.Equal(ExpectedDocumentationHosts, documentation);
        Assert.DoesNotContain(DemoTextLists.AllowedHosts(), host => { return documentation.Contains(host); });
    }

    /// <summary>Every stem in the shipped lists has at least the minimum letters, so the screen can be built.</summary>
    [Fact]
    public void Every_shipped_stem_is_long_enough()
    {
        var stems = DemoTextLists.BlockedTerms().Where(term => { return term.EndsWith('*'); }).ToList();

        Assert.NotEmpty(stems);
        Assert.All(stems, stem => { Assert.True(stem.Length - 1 >= 3, stem); });
    }

    /// <summary>Words that are ordinary code or ops text are not on the lists.</summary>
    [Fact]
    public void Ordinary_markers_are_not_blocked_terms()
    {
        var terms = DemoTextLists.BlockedTerms();

        Assert.DoesNotContain("xxx", terms);
        Assert.DoesNotContain("click here", terms);
    }

    /// <summary>The shipped screen refuses one term of each language file, so no file is shipped unread.</summary>
    [Fact]
    public void The_shipped_screen_refuses_a_term_from_each_language()
    {
        Assert.Equal(DemoTextVerdict.BlockedTerm, DemoTextScreen.Default.Judge("casino"));
        Assert.Equal(DemoTextVerdict.BlockedTerm, DemoTextScreen.Default.Judge("казино"));
        Assert.Equal(DemoTextVerdict.BlockedTerm, DemoTextScreen.Default.Judge("կազինո"));
        Assert.Equal(Cultures.Length, DemoTextLists.BlockedTermResources().Count);
    }
}
