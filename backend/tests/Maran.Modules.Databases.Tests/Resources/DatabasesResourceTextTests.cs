using Maran.Modules.Databases.Tests.TestSupport;

namespace Maran.Modules.Databases.Tests.Resources;

/// <summary>
/// What the reader actually reads, in all three languages this panel ships: the module's error
/// sentences and the grant-repair refusal text beside them.
/// </summary>
/// <remarks>
/// The handler tests assert a refusal's CODE, because <c>Error</c> carries no message. These assert the
/// SENTENCES, because the sentence is where a promise lives that a code cannot express: that no message
/// this module ships names a path on the host, and that every translated entry is a translation rather
/// than a key, the English, or a neighbour's text pasted into the wrong slot.
/// </remarks>
public sealed class DatabasesResourceTextTests
{
    /// <summary>The two resx families this module ships.</summary>
    private static readonly string[] Families = ["ErrorMessages", "DisplayNames"];

    /// <summary>The absolute-path roots a message may never name.</summary>
    /// <remarks>
    /// The host's own top level rather than the folders a database server happens to use, for the reason
    /// the FTPS module's sweep gives: the root a message here is likeliest to grow is the one whose
    /// disclosure is about SOMEBODY ELSE. The grant-repair text is about rows that belong to other
    /// tenants, and a sentence that told an operator where another customer's data files sit would name
    /// that customer (rules/security.md item 8).
    /// </remarks>
    private static readonly string[] ForbiddenPathRoots =
        ["/etc/", "/var/", "/home/", "/root/", "/usr/", "/srv/", "/opt/", "/tmp/"];

    /// <summary>No message in any locale of any family carries a filesystem path.</summary>
    /// <remarks>
    /// All three locales, because a path pasted into the Russian translation alone would pass an
    /// English-only sweep. The per-locale non-empty assertion is the vacuity guard on the axis that can
    /// go blind — a sweep over zero entries proves nothing — and a missing FILE throws in the reader
    /// rather than arriving here as an empty locale.
    /// </remarks>
    [Fact]
    public void No_message_in_any_locale_of_any_family_carries_a_filesystem_path()
    {
        Assert.NotEmpty(Families);

        foreach (var family in Families)
        {
            var byLocale = ResourceValues.ValuesByLocale(family);
            Assert.Equal(3, byLocale.Count);

            foreach (var entries in byLocale)
            {
                Assert.NotEmpty(entries);
                Assert.All(entries, value =>
                {
                    Assert.False(NamesAPath(value), $"{family}: {value}");
                });
            }
        }
    }

    /// <summary>The path sweep can see a planted path at every root it claims to guard.</summary>
    /// <remarks>
    /// The positive control the sweep owes, and it drives the sweep's OWN predicate rather than
    /// restating the comparison. Every root is planted, not one: a control exercising only
    /// <c>/etc/</c> would go on passing if the other seven were added to the list and dropped from the
    /// comparison.
    /// </remarks>
    [Fact]
    public void The_path_sweep_finds_a_planted_path_at_every_root_it_guards()
    {
        Assert.NotEmpty(ForbiddenPathRoots);

        Assert.All(ForbiddenPathRoots, root =>
        {
            var planted = ResourceValues.ValuesByLocale("DisplayNames")[1]
                .Append($"Compare it with the grant table under {root}mysql and correct it.")
                .ToList();

            Assert.Contains(planted, NamesAPath);
        });
    }

    /// <summary>The path sweep sees the neighbour's home a grant-repair sentence would most likely name.</summary>
    /// <remarks>
    /// The root whose disclosure is about a DIFFERENT customer: a sentence quoting
    /// <c>/home/bob/mysql</c> tells the reader that an account named <c>bob</c> exists on this server,
    /// which is exactly the class of fact the refused rows on this screen are already skirting. The
    /// sweep above proves no shipped sentence contains it; this proves the sweep would notice if one
    /// did.
    /// </remarks>
    [Fact]
    public void The_path_sweep_finds_a_planted_neighbours_home()
    {
        Assert.True(NamesAPath("The stored files are under /home/bob/mysql."));
        Assert.False(NamesAPath("The stored files are under the account's own home."));
    }

    /// <summary>Every translated entry is a translation: not the key, not the English, not a neighbour's.</summary>
    /// <remarks>
    /// <para>
    /// The three ways a locale file actually goes wrong: a key left untranslated ships the identifier to
    /// the reader; a key copied but not translated ships English into a Russian screen; and the
    /// realistic third — the one a spell checker and a key-parity sweep both miss — is a NEIGHBOURING
    /// entry pasted into the wrong slot, which reads as fluent Armenian and answers the wrong question.
    /// </para>
    /// <para>
    /// Keys and values are zipped by POSITION, which is sound because both are read out of the same
    /// <c>&lt;data&gt;</c> sequence of the same document.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_translated_entry_is_a_translation_and_not_a_neighbours_text()
    {
        foreach (var family in Families)
        {
            var keys = ResourceValues.KeysByLocale(family);
            var values = ResourceValues.ValuesByLocale(family);
            Assert.Equal(3, values.Count);

            var neutral = Pairs(keys[0], values[0]);

            for (var locale = 1; locale < values.Count; locale++)
            {
                var translated = Pairs(keys[locale], values[locale]);
                Assert.NotEmpty(translated);
                Assert.Equal(translated.Count, translated.Values.Distinct(StringComparer.Ordinal).Count());

                foreach (var entry in translated)
                {
                    Assert.NotEqual(entry.Key, entry.Value);
                    Assert.NotEqual(neutral[entry.Key], entry.Value);
                }
            }
        }
    }

    /// <summary>The distinctness check sees a neighbour's sentence pasted into another slot.</summary>
    /// <remarks>
    /// The positive control the check above owes. It plants exactly the defect the check hunts — one
    /// entry's text sitting in another entry's slot — and asserts the detector finds it. Without this, a
    /// table that happened to contain no duplicates for an unrelated reason would certify the absence of
    /// a defect the assertion could never have seen.
    /// </remarks>
    [Fact]
    public void A_neighbours_text_pasted_into_another_slot_is_detected()
    {
        var values = ResourceValues.ValuesByLocale("DisplayNames")[2].ToList();
        values[0] = values[1];

        Assert.NotEqual(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Whether a sentence names an absolute path on the host.</summary>
    /// <param name="value">One message as its reader would see it.</param>
    /// <returns>True when the text contains any of <see cref="ForbiddenPathRoots"/>.</returns>
    /// <remarks>
    /// One predicate, shared by the sweep and by its positive controls, so that the thing proved
    /// observable is the thing the sweep runs.
    /// </remarks>
    private static bool NamesAPath(string value)
    {
        return ForbiddenPathRoots.Any(root =>
        {
            return value.Contains(root, StringComparison.Ordinal);
        });
    }

    /// <summary>Zips one locale's keys and values into a lookup.</summary>
    /// <param name="keys">The locale's keys, in document order.</param>
    /// <param name="values">The same locale's values, in the same document order.</param>
    /// <returns>The locale's entries by key.</returns>
    private static Dictionary<string, string> Pairs(IReadOnlyList<string> keys, IReadOnlyList<string> values)
    {
        Assert.Equal(keys.Count, values.Count);

        return keys.Zip(values).ToDictionary(
            pair => { return pair.First; },
            pair => { return pair.Second; },
            StringComparer.Ordinal);
    }
}
