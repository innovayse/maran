using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>
/// The content filter refuses blocked terms in every spelling it promises to see through and links to hosts off
/// the allow-list, and passes everything else: each refusal has a clean control beside it.
/// </summary>
public sealed class DemoTextScreenTests
{
    /// <summary>The screen over the lists the demo ships, which is the one that runs.</summary>
    private static readonly DemoTextScreen Shipped = DemoTextScreen.Default;

    /// <summary>A text and the verdict the shipped screen must give it.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("Buy casino chips", DemoTextVerdict.BlockedTerm)]
    [InlineData("CASINO", DemoTextVerdict.BlockedTerm)]
    [InlineData("a Casino.", DemoTextVerdict.BlockedTerm)]
    [InlineData("FREE   money", DemoTextVerdict.BlockedTerm)]
    [InlineData("v.i.a.g.r.a", DemoTextVerdict.BlockedTerm)]
    [InlineData("v i a g r a", DemoTextVerdict.BlockedTerm)]
    [InlineData("víagra", DemoTextVerdict.BlockedTerm)]
    [InlineData("v​iagra", DemoTextVerdict.BlockedTerm)]
    [InlineData("ｖｉａｇｒａ", DemoTextVerdict.BlockedTerm)]
    [InlineData("5h1t", DemoTextVerdict.BlockedTerm)]
    [InlineData("vіagra", DemoTextVerdict.BlockedTerm)]
    [InlineData("порно", DemoTextVerdict.BlockedTerm)]
    [InlineData("КАЗИНО", DemoTextVerdict.BlockedTerm)]
    [InlineData("Պոռնո", DemoTextVerdict.BlockedTerm)]
    [InlineData("պnռնn", DemoTextVerdict.BlockedTerm)]
    [InlineData("Քոռնո", DemoTextVerdict.Clean)]
    [InlineData("ՆՕՐՆՕ", DemoTextVerdict.Clean)]
    [InlineData("Scunthorpe", DemoTextVerdict.Clean)]
    [InlineData("Cassino", DemoTextVerdict.Clean)]
    [InlineData("shiitake mushrooms", DemoTextVerdict.Clean)]
    [InlineData("a b c", DemoTextVerdict.Clean)]
    [InlineData("free", DemoTextVerdict.Clean)]
    [InlineData("money free", DemoTextVerdict.Clean)]
    [InlineData("A garden blog about tomatoes", DemoTextVerdict.Clean)]
    [InlineData("", DemoTextVerdict.Clean)]
    public void Terms_are_matched_through_case_spacing_unicode_and_swaps_but_only_as_whole_words(
        string text,
        DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>A missing text is clean, not an error.</summary>
    [Fact]
    public void A_null_text_is_clean()
    {
        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge(null));
    }

    /// <summary>A text and the verdict the shipped screen must give its links.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("visit http://evil.com/x", DemoTextVerdict.ForeignUrl)]
    [InlineData("HTTPS://EVIL.COM", DemoTextVerdict.ForeignUrl)]
    [InlineData("ftp://files.evil.net/a", DemoTextVerdict.ForeignUrl)]
    [InlineData("https://example.com@evil.com/", DemoTextVerdict.ForeignUrl)]
    [InlineData("https://example.com.evil.com/", DemoTextVerdict.ForeignUrl)]
    [InlineData("http://evil.com:8080", DemoTextVerdict.ForeignUrl)]
    [InlineData("http://[2001:db8::1]/", DemoTextVerdict.ForeignUrl)]
    [InlineData("http://", DemoTextVerdict.ForeignUrl)]
    [InlineData("http:\\\\evil.com", DemoTextVerdict.ForeignUrl)]
    [InlineData("ht​tps://evil.com", DemoTextVerdict.ForeignUrl)]
    [InlineData("ｈｔｔｐ：／／evil.com", DemoTextVerdict.ForeignUrl)]
    [InlineData("go to www.evil.com now", DemoTextVerdict.ForeignUrl)]
    [InlineData("javascript:alert(1)", DemoTextVerdict.ForeignUrl)]
    [InlineData("JaVaScRiPt:alert(1)", DemoTextVerdict.ForeignUrl)]
    [InlineData("data:text/html,<b>x</b>", DemoTextVerdict.ForeignUrl)]
    [InlineData("https://example.com/page", DemoTextVerdict.Clean)]
    [InlineData("HTTP://WWW.EXAMPLE.ORG/A", DemoTextVerdict.Clean)]
    [InlineData("https://shop.example.net", DemoTextVerdict.Clean)]
    [InlineData("https://user@example.com/", DemoTextVerdict.Clean)]
    [InlineData("www.example.com", DemoTextVerdict.Clean)]
    [InlineData("v=DMARC1; p=none; rua=mailto:dmarc@example.com", DemoTextVerdict.Clean)]
    [InlineData("metadata:foo", DemoTextVerdict.Clean)]
    [InlineData("data: five rows", DemoTextVerdict.Clean)]
    [InlineData("evil.com", DemoTextVerdict.Clean)]
    [InlineData("index.php", DemoTextVerdict.Clean)]
    public void Links_are_refused_unless_their_host_is_allowed(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>Text that is not well-formed Unicode is refused: a screen that fails open is a switch.</summary>
    [Fact]
    public void Malformed_unicode_is_unreadable_and_not_passed()
    {
        Assert.Equal(DemoTextVerdict.Unreadable, Shipped.Judge("fine text \uD800"));
        Assert.Equal(DemoTextVerdict.Unreadable, Shipped.Judge("\uDC00 low surrogate first"));
    }

    /// <summary>The accepting control: a well-formed pair outside the basic plane is read, not refused.</summary>
    [Fact]
    public void A_surrogate_pair_is_well_formed_and_clean()
    {
        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge("fine text \uD83D\uDE00"));
    }

    /// <summary>A term of several words matches those words side by side and in order only.</summary>
    [Fact]
    public void A_multi_word_term_needs_its_words_in_order()
    {
        var screen = new DemoTextScreen(["foo bar"], []);

        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("a foo, bar b"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("bar foo"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("foo"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("foo baz bar"));
    }

    /// <summary>A host matches itself and the names under it, and nothing that merely ends alike.</summary>
    [Fact]
    public void An_allowed_host_matches_itself_and_its_subdomains_only()
    {
        var screen = new DemoTextScreen([], ["allowed.test"]);

        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("http://allowed.test/"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("http://a.b.allowed.test/"));
        Assert.Equal(DemoTextVerdict.ForeignUrl, screen.Judge("http://notallowed.test/"));
        Assert.Equal(DemoTextVerdict.ForeignUrl, screen.Judge("http://allowed.test.evil/"));
    }

    /// <summary>With no terms listed a term is not found, and with no hosts listed every link is foreign.</summary>
    [Fact]
    public void Empty_lists_block_no_term_and_allow_no_host()
    {
        var screen = new DemoTextScreen([], []);

        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("casino"));
        Assert.Equal(DemoTextVerdict.ForeignUrl, screen.Judge("http://example.com"));
    }

    /// <summary>A very long text is judged in bounded time and a term at its far end is still found.</summary>
    [Fact]
    public void A_long_text_is_screened_to_its_end()
    {
        var padding = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 40_000));

        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge(padding));
        Assert.Equal(DemoTextVerdict.BlockedTerm, Shipped.Judge(padding + "casino"));
    }

    /// <summary>Inflected forms of a stem are found in every language, and the whole-word controls beside them pass.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("you are fucking late", DemoTextVerdict.BlockedTerm)]
    [InlineData("Fuckers", DemoTextVerdict.BlockedTerm)]
    [InlineData("shitty", DemoTextVerdict.BlockedTerm)]
    [InlineData("bitches", DemoTextVerdict.BlockedTerm)]
    [InlineData("online casinos", DemoTextVerdict.BlockedTerm)]
    [InlineData("pornhub", DemoTextVerdict.BlockedTerm)]
    [InlineData("escorts", DemoTextVerdict.BlockedTerm)]
    [InlineData("nazis", DemoTextVerdict.BlockedTerm)]
    [InlineData("хуя", DemoTextVerdict.BlockedTerm)]
    [InlineData("пизды", DemoTextVerdict.BlockedTerm)]
    [InlineData("бляди", DemoTextVerdict.BlockedTerm)]
    [InlineData("ебал", DemoTextVerdict.BlockedTerm)]
    [InlineData("ебаный", DemoTextVerdict.BlockedTerm)]
    [InlineData("суки", DemoTextVerdict.BlockedTerm)]
    [InlineData("пиздец", DemoTextVerdict.BlockedTerm)]
    [InlineData("порнография", DemoTextVerdict.BlockedTerm)]
    [InlineData("պոռնոները", DemoTextVerdict.BlockedTerm)]
    [InlineData("կազինոյում", DemoTextVerdict.BlockedTerm)]
    [InlineData("nazism", DemoTextVerdict.Clean)]
    [InlineData("escorted the guest", DemoTextVerdict.Clean)]
    [InlineData("Scunthorpe cunning", DemoTextVerdict.Clean)]
    [InlineData("the classic assistant", DemoTextVerdict.Clean)]
    [InlineData("сукно", DemoTextVerdict.Clean)]
    [InlineData("сук", DemoTextVerdict.Clean)]
    [InlineData("хлеб", DemoTextVerdict.Clean)]
    [InlineData("пиво", DemoTextVerdict.Clean)]
    [InlineData("հաց ու կաթ", DemoTextVerdict.Clean)]
    public void A_stem_finds_the_inflected_forms_and_a_whole_word_term_does_not(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>A stem matches a word only at its start, so a word that merely contains it passes.</summary>
    [Fact]
    public void A_stem_matches_the_start_of_a_word_only()
    {
        var screen = new DemoTextScreen(["cunt*"], []);

        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("cunts"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("scunthorpe"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("cun"));
    }

    /// <summary>A multi-word stem needs its leading words whole and in order, then a word that starts with the stem.</summary>
    [Fact]
    public void A_multi_word_stem_applies_to_its_last_word()
    {
        var screen = new DemoTextScreen(["quick money*"], []);

        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("so quick moneymaker"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("quicker money"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("money quick"));
    }

    /// <summary>A stem too short to be safe is rejected when the screen is built, not silently accepted.</summary>
    [Fact]
    public void A_stem_shorter_than_three_letters_is_refused_at_construction()
    {
        Assert.Throws<ArgumentException>(() => { return new DemoTextScreen(["ab*"], []); });
        Assert.NotNull(new DemoTextScreen(["abc*"], []));
    }

    /// <summary>Characters written inside a word to break it up are read through; ordinary punctuation still separates.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("fu.ck", DemoTextVerdict.BlockedTerm)]
    [InlineData("fu-ck", DemoTextVerdict.BlockedTerm)]
    [InlineData("fu*ck", DemoTextVerdict.BlockedTerm)]
    [InlineData("sh_it", DemoTextVerdict.BlockedTerm)]
    [InlineData("c.a.s.i.n.o", DemoTextVerdict.BlockedTerm)]
    [InlineData("b+tch", DemoTextVerdict.Clean)]
    [InlineData("fu ck", DemoTextVerdict.Clean)]
    [InlineData("fu, ck", DemoTextVerdict.Clean)]
    [InlineData("the end. Casing", DemoTextVerdict.Clean)]
    [InlineData("a-b", DemoTextVerdict.Clean)]
    public void Separators_written_inside_a_word_do_not_hide_it(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>Digit swaps are read inside words with letters and never in a number, a port or a cron field.</summary>
    [Fact]
    public void Digits_are_folded_to_letters_only_inside_words_that_have_letters()
    {
        var screen = new DemoTextScreen(["ass", "ste"], []);

        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("a55"));
        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("@ss"));
        Assert.Equal(DemoTextVerdict.BlockedTerm, screen.Judge("a $ $"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("455"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("5 7 3"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("*/5 7 3 * *"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("port 455, pid 573"));
        Assert.Equal(DemoTextVerdict.Clean, screen.Judge("$55"));
    }

    /// <summary>Look-alike letters and invisible separators the first fold did not know are mapped or dropped.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("cas\u0131no", DemoTextVerdict.BlockedTerm)]
    [InlineData("p\u00F8rn", DemoTextVerdict.BlockedTerm)]
    [InlineData("\u04BBentai", DemoTextVerdict.BlockedTerm)]
    [InlineData("c\u03B1sino", DemoTextVerdict.BlockedTerm)]
    [InlineData("cas\u03B9no", DemoTextVerdict.BlockedTerm)]
    [InlineData("ca\u3164sino", DemoTextVerdict.BlockedTerm)]
    [InlineData("ca\u2800sino", DemoTextVerdict.BlockedTerm)]
    [InlineData("cas\u115Fino", DemoTextVerdict.BlockedTerm)]
    [InlineData("ca\U000E0041sino", DemoTextVerdict.BlockedTerm)]
    [InlineData("\u0561\u0562\u0563", DemoTextVerdict.Clean)]
    [InlineData("ca sino", DemoTextVerdict.Clean)]
    [InlineData("casno", DemoTextVerdict.Clean)]
    [InlineData("lodz", DemoTextVerdict.Clean)]
    public void Wider_lookalikes_and_invisible_separators_are_folded(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>The words the lists used to refuse for code and ops text are clean now.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("XXX: fix before release")]
    [InlineData("xxx.example.com")]
    [InlineData("<a href=\"#top\">click here</a>")]
    [InlineData("class Casing {}")]
    public void Ordinary_markers_in_code_and_ops_text_are_clean(string text)
    {
        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge(text));
    }

    /// <summary>
    /// A host that the authority scan used to cut short is read the way a browser reads it: a quote, a backtick or a
    /// control character inside the host keeps it a different host, so the link is refused.
    /// </summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("http://example.com'.evil.com/")]
    [InlineData("http://example.com\"x.evil.com")]
    [InlineData("http://example.com`.evil.com")]
    [InlineData("http://example.com\t.evil.com")]
    [InlineData("http://example.com\n.evil.com")]
    [InlineData("http://example.com\r\n.evil.com")]
    [InlineData("http://example.com%2eevil.com")]
    [InlineData("http://ex\u00E4mple.com/")]
    [InlineData("http://exa\u0308mple.com/")]
    [InlineData("http://\u0435xample.com/")]
    [InlineData("http://xn--exmple-cua.com/")]
    [InlineData("http://example..com/")]
    [InlineData("http://.example.com/")]
    public void A_host_is_read_as_a_browser_reads_it(string text)
    {
        Assert.Equal(DemoTextVerdict.ForeignUrl, Shipped.Judge(text));
    }

    /// <summary>The inverse controls: the same links with a clean host, in the shapes prose and markup put around them.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("<a href=\"https://example.com\">x</a>")]
    [InlineData("\"https://example.com\"")]
    [InlineData("'https://example.com'")]
    [InlineData("(https://example.com).")]
    [InlineData("see https://example.com, then")]
    [InlineData("[https://example.com]")]
    [InlineData("http://exa\u200Bmple.com/")]
    [InlineData("http://example.com\t")]
    [InlineData("http://example.com ")]
    [InlineData("http://example.com/\n.evil.com")]
    [InlineData("http://example.com//evil.com")]
    public void A_clean_host_is_clean_whatever_surrounds_it(string text)
    {
        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge(text));
    }

    /// <summary>A script scheme broken up by a control character is the same scheme to a browser.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("javascript:\nalert(1)")]
    [InlineData("javascript: alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("magnet:?xt=urn:btih:abc")]
    public void A_script_scheme_is_refused_however_it_is_broken_up(string text)
    {
        Assert.Equal(DemoTextVerdict.ForeignUrl, Shipped.Judge(text));
    }

    /// <summary>Links written the shorter ways a browser still follows, and links nested after another scheme.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("http:/evil.com")]
    [InlineData("http:evil.com")]
    [InlineData("https:///evil.com")]
    [InlineData("//evil.com/x")]
    [InlineData("see //evil.com")]
    [InlineData("<a href=//evil.com>x</a>")]
    [InlineData("\\/evil.com")]
    [InlineData("blob:https://evil.com/x")]
    [InlineData("view-source:http://evil.com")]
    public void Short_and_nested_link_forms_are_refused(string text)
    {
        Assert.Equal(DemoTextVerdict.ForeignUrl, Shipped.Judge(text));
    }

    /// <summary>The inverse controls: text that merely looks like those forms is not a link.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("// TODO: fix")]
    [InlineData("//TODO")]
    [InlineData("x = 1 // note")]
    [InlineData("see /usr//bin")]
    [InlineData("a//b.c")]
    [InlineData("C:\\Users\\me")]
    [InlineData("C:/dir")]
    [InlineData("note:foo")]
    [InlineData("http: nothing follows")]
    [InlineData("//example.com/x")]
    [InlineData("http:/example.com")]
    public void Text_that_only_looks_like_a_link_is_clean(string text)
    {
        Assert.Equal(DemoTextVerdict.Clean, Shipped.Judge(text));
    }

    /// <summary>Loopback is not an allowed host: a link a server later fetches must not reach its own services.</summary>
    /// <param name="text">The text a visitor typed.</param>
    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("curl http://127.0.0.1:8080/cron.php")]
    [InlineData("http://x.localhost/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://0.0.0.0/")]
    public void Loopback_is_a_foreign_host(string text)
    {
        Assert.Equal(DemoTextVerdict.ForeignUrl, Shipped.Judge(text));
    }

    /// <summary>The documentation namespaces are allowed for http and https; the same names under another scheme or host are not.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>", DemoTextVerdict.Clean)]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1.dtd\">", DemoTextVerdict.Clean)]
    [InlineData("Licensed under http://www.apache.org/licenses/LICENSE-2.0", DemoTextVerdict.Clean)]
    [InlineData("\"@context\": \"https://schema.org\"", DemoTextVerdict.Clean)]
    [InlineData("ftp://www.w3.org/x", DemoTextVerdict.ForeignUrl)]
    [InlineData("s3://www.w3.org/x", DemoTextVerdict.ForeignUrl)]
    [InlineData("http://w3.org.evil.com/", DemoTextVerdict.ForeignUrl)]
    [InlineData("http://notw3.org/", DemoTextVerdict.ForeignUrl)]
    [InlineData("https://evilschema.org/", DemoTextVerdict.ForeignUrl)]
    [InlineData("https://schema.org@evil.com/", DemoTextVerdict.ForeignUrl)]
    public void Documentation_namespaces_are_allowed_for_web_links_only(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>Other schemes are links subject to the host rule, so only an example host is allowed.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("s3://bucket/key", DemoTextVerdict.ForeignUrl)]
    [InlineData("git://github.com/x/y.git", DemoTextVerdict.ForeignUrl)]
    [InlineData("php://input", DemoTextVerdict.ForeignUrl)]
    [InlineData("redis://cache:6379", DemoTextVerdict.ForeignUrl)]
    [InlineData("ssh://git@github.com/x", DemoTextVerdict.ForeignUrl)]
    [InlineData("file:///etc/passwd", DemoTextVerdict.ForeignUrl)]
    [InlineData("s3://example.com/key", DemoTextVerdict.Clean)]
    [InlineData("git://example.net/x.git", DemoTextVerdict.Clean)]
    [InlineData("php://example.org", DemoTextVerdict.Clean)]
    public void Other_schemes_need_an_example_host(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary><c>data:</c> is a scheme only at the start of a URL and with a media type; elsewhere it is a word.</summary>
    /// <param name="text">The text a visitor typed.</param>
    /// <param name="expected">The verdict.</param>
    [Theory]
    [InlineData("data:text/html;base64,AAA", DemoTextVerdict.ForeignUrl)]
    [InlineData("<img src=\"data:image/png;base64,AAA\">", DemoTextVerdict.ForeignUrl)]
    [InlineData("open data:,hello", DemoTextVerdict.ForeignUrl)]
    [InlineData("background: url(data:image/png;base64,AAA)", DemoTextVerdict.Clean)]
    [InlineData("const o = {data:1}", DemoTextVerdict.Clean)]
    [InlineData("x = {data:'a/b'}", DemoTextVerdict.Clean)]
    [InlineData("data:5", DemoTextVerdict.Clean)]
    [InlineData("metadata:text/plain", DemoTextVerdict.Clean)]
    [InlineData("log: data:ok", DemoTextVerdict.Clean)]
    public void A_data_url_needs_a_media_type_at_the_start_of_a_url(string text, DemoTextVerdict expected)
    {
        Assert.Equal(expected, Shipped.Judge(text));
    }

    /// <summary>The documentation list applies only where it is given: a screen built without it refuses those links.</summary>
    [Fact]
    public void A_screen_without_documentation_hosts_refuses_them()
    {
        var without = new DemoTextScreen([], ["example.com"]);
        var with = new DemoTextScreen([], ["example.com"], ["w3.org"]);

        Assert.Equal(DemoTextVerdict.ForeignUrl, without.Judge("http://www.w3.org/x"));
        Assert.Equal(DemoTextVerdict.Clean, with.Judge("http://www.w3.org/x"));
        Assert.Equal(DemoTextVerdict.ForeignUrl, with.Judge("ftp://www.w3.org/x"));
    }
}
