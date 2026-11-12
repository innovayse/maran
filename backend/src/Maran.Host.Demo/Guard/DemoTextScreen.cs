using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Guard.Text;
using Maran.Host.Demo.Resources;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// The public demo's content filter for text a visitor types and the demo then stores and shows to the next
/// visitor: it answers whether the text carries a blocked term or a link to somewhere that is not on the
/// allow-list. A pure rule over strings: no outbound call, no state, the same answer for the same text.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it refuses.</b> (1) A term from the lists in <c>Resources/DemoBlockedTerms.*.txt</c>, every
/// language's file read together; (2) a link whose host is not on <c>Resources/DemoAllowedHosts.txt</c>
/// (or, for <c>http</c> and <c>https</c>, <c>Resources/DemoDocumentationHosts.txt</c>), and a scheme that runs
/// script or carries content in place of naming a host. What counts as a link and how its host is read is
/// <see cref="DemoLinkScreen"/>. A bare <c>name.tld</c> with no scheme and no <c>www.</c> is NOT refused: a
/// domain field, a file name and a sentence mentioning a domain are all that shape, and refusing them would
/// refuse the demo's own use. That is the stated residual of threat note T7.
/// </para>
/// <para>
/// <b>How a term is matched.</b> Both the text and the term are folded the same way (<see cref="DemoTextFold"/>),
/// so a trick that changes the text changes the term identically: compatibility forms (full-width letters),
/// accents, invisible and format characters removed, case folded. Words are runs of letters and digits; a run
/// of three or more single characters (<c>v.i.a.g.r.a</c>) is read as one word, and a second reading drops the
/// characters written inside words to break them (<c>fu*ck</c>, <c>sh_it</c>, <c>fu-ck</c>). A listed term
/// matches whole words, or, when it ends in <c>*</c>, any word that starts with it, so inflected forms
/// (<c>fucking</c>, <c>пиздец</c>) are found while <c>Scunthorpe</c> is not (<see cref="DemoTermIndex"/>). A
/// second, stricter fold also maps look-alike letters of other scripts to Latin and digit and symbol swaps
/// (<c>5h1t</c>) inside words that have a letter, so a bare number such as a port is never read as a word.
/// </para>
/// <para>
/// <b>It cannot see</b> a term spelled in a way neither fold maps (doubled letters such as <c>fuuuck</c>, a
/// paraphrase), a term split across two fields, a multi-word term whose words are in different fields, or text
/// inside an image or an archive; it is a floor, not a moderator, and the abuse contact on the banner is the
/// other half.
/// </para>
/// </remarks>
public sealed class DemoTextScreen
{
    /// <summary>Whether the characters written inside words to break them are dropped: the two readings tried.</summary>
    private static readonly bool[] JoinedReadings = [false, true];

    /// <summary>The screen over the lists shipped in the assembly.</summary>
    public static readonly DemoTextScreen Default = new(
        DemoTextLists.BlockedTerms(),
        DemoTextLists.AllowedHosts(),
        DemoTextLists.DocumentationHosts());

    /// <summary>The blocked terms under the plain fold.</summary>
    private readonly DemoTermIndex _plainTerms;

    /// <summary>The blocked terms under the stricter fold.</summary>
    private readonly DemoTermIndex _strictTerms;

    /// <summary>The link rules.</summary>
    private readonly DemoLinkScreen _links;

    /// <summary>Builds a screen over explicit lists.</summary>
    /// <param name="blockedTerms">The terms to refuse (a trailing <c>*</c> makes the last word a stem); blank entries are ignored.</param>
    /// <param name="allowedHosts">The hosts any link may name; blank entries are ignored.</param>
    /// <param name="documentationHosts">The hosts only an <c>http</c> or <c>https</c> link may name; none by default.</param>
    /// <exception cref="ArgumentException">A stem is shorter than <see cref="DemoTermIndex.MinimumStemLength"/> letters.</exception>
    public DemoTextScreen(
        IEnumerable<string> blockedTerms,
        IEnumerable<string> allowedHosts,
        IEnumerable<string>? documentationHosts = null)
    {
        ArgumentNullException.ThrowIfNull(blockedTerms);
        ArgumentNullException.ThrowIfNull(allowedHosts);

        var terms = blockedTerms.ToList();
        _plainTerms = new DemoTermIndex(terms, strict: false);
        _strictTerms = new DemoTermIndex(terms, strict: true);
        _links = new DemoLinkScreen(allowedHosts, documentationHosts ?? Array.Empty<string>());
    }

    /// <summary>Judges one piece of visitor-typed text.</summary>
    /// <param name="text">The text; <c>null</c> and empty are clean.</param>
    /// <returns>
    /// What the screen found. Text the screen cannot read (malformed Unicode) or cannot finish judging in time
    /// is <see cref="DemoTextVerdict.Unreadable"/>, refused rather than passed, because a screen that fails open is one a crafted input switches off.
    /// </returns>
    public DemoTextVerdict Judge(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return DemoTextVerdict.Clean;
        }

        if (!IsWellFormed(text))
        {
            return DemoTextVerdict.Unreadable;
        }

        try
        {
            if (_links.HasForeignUrl(DemoTextFold.ForLinks(text)))
            {
                return DemoTextVerdict.ForeignUrl;
            }

            return ContainsTerm(DemoTextFold.Plain(text)) ? DemoTextVerdict.BlockedTerm : DemoTextVerdict.Clean;
        }
        catch (RegexMatchTimeoutException)
        {
            return DemoTextVerdict.Unreadable;
        }
    }

    /// <summary>Whether every surrogate in the text is paired, so the folds can read it.</summary>
    /// <param name="text">The text.</param>
    /// <returns><c>false</c> for a lone surrogate.</returns>
    private static bool IsWellFormed(string text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out var consumed) != OperationStatus.Done)
            {
                return false;
            }

            rest = rest[consumed..];
        }

        return true;
    }

    /// <summary>Whether a term appears in the text under either fold and either reading.</summary>
    /// <param name="plain">The text under the plain fold.</param>
    /// <returns><c>true</c> when any term is found.</returns>
    private bool ContainsTerm(string plain)
    {
        var strict = DemoTextFold.Strict(plain);
        foreach (var joined in JoinedReadings)
        {
            if (_plainTerms.Matches(DemoTextFold.TextWords(plain, strict: false, joined))
                || _strictTerms.Matches(DemoTextFold.TextWords(strict, strict: true, joined)))
            {
                return true;
            }
        }

        return false;
    }
}
