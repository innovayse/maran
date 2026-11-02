using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Tests.TestSupport;

namespace Maran.Modules.Databases.Tests.Services;

/// <summary>
/// Covers the two sentences a refused grant-table row carries, and — the part that makes this a fix
/// rather than a sample — that EVERY reason the agent can report has both of them.
/// </summary>
/// <remarks>
/// The set is walked, not sampled, because a reason with no entry does not fail, does not warn and
/// does not look like a defect from the backend: the localizer answers with the key itself, so an
/// operator reads <c>GrantRepairRefusalPartiallyOrUnfamiliarlyEscaped</c> where a sentence belongs, in
/// all three languages. The closed set is the module's own <see cref="GrantRefusalReason"/>, so a reason
/// added to it fails here rather than reaching a screen as a key.
/// </remarks>
public sealed class GrantRepairRefusalDisplayNamesTests
{
    /// <summary>The three cultures this panel ships, neutral first.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>Every refusal the agent can report has a name and an action in every language.</summary>
    /// <remarks>
    /// Read through the REAL resource files by culture, so a missing satellite or an untranslated entry
    /// is visible here rather than in a browser. There is no entry for a reason the module does not
    /// have: the agent client refuses a reply naming one, so no row ever reaches the resolver without
    /// a member of the enum.
    /// </remarks>
    [Fact]
    public void Every_refusal_the_agent_can_report_has_a_name_and_an_action_in_every_language()
    {
        var reasons = Enum.GetValues<GrantRefusalReason>();
        Assert.NotEmpty(reasons);

        var problems = new List<string>();
        foreach (var culture in Cultures)
        {
            using var _ = new CultureScope(culture);
            var text = DatabasesTestContext.RefusalText();

            foreach (var reason in reasons)
            {
                // A missing entry makes the localizer answer with the key it looked up.
                if (string.Equals(text.NameOf(reason), "GrantRepairRefusal" + reason, StringComparison.Ordinal))
                {
                    problems.Add($"'{reason}' has no name in {culture}; the screen prints the key.");
                }

                if (string.Equals(text.AdviceOf(reason), "GrantRepairAdvice" + reason, StringComparison.Ordinal))
                {
                    problems.Add($"'{reason}' has no advice in {culture}; the row states a verdict with no next step.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "A refusal reaches the operator's screen as a resource key (rules/vue.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    /// <summary>The walk reads the enum it claims to, and the localizer can miss.</summary>
    /// <remarks>
    /// The positive control the walk above owes, on the axis that can go blind (rules/testing.md). Two
    /// halves: the enumeration really contains the values this build ships — a reflection query that
    /// returned nothing would make the walk pass over nothing at all — and the localizer really answers
    /// with the key for a name it has no entry for, which is the condition the walk detects. A localizer
    /// that answered with a sentence for every input would make the walk green forever.
    /// </remarks>
    [Fact]
    public void The_walk_reads_the_enum_it_claims_to_and_the_localizer_can_miss()
    {
        var reasons = Enum.GetValues<GrantRefusalReason>();

        Assert.Contains(GrantRefusalReason.HostIsNotLocalhost, reasons);
        Assert.Contains(GrantRefusalReason.NotThePanelsNaming, reasons);
        Assert.Contains(GrantRefusalReason.UnrecognisedPrivileges, reasons);
        Assert.Contains(GrantRefusalReason.PartiallyOrUnfamiliarlyEscaped, reasons);
        Assert.Equal(4, reasons.Length);

        var text = DatabasesTestContext.RefusalText();
        Assert.Equal("GrantRepairRefusal99", text.NameOf((GrantRefusalReason)99));
        Assert.Equal("GrantRepairAdvice99", text.AdviceOf((GrantRefusalReason)99));
    }

    /// <summary>The refusal for a grant from another host tells the operator it is still wide.</summary>
    /// <remarks>
    /// One exact sentence, in Russian, because two promises live in it that a code cannot express: that
    /// the panel changed nothing, and that the row STILL carries the wide pattern. A row reported as
    /// merely "skipped" would read as a row that needs no attention, which is the opposite of the truth
    /// — this is the one refusal whose grant is reachable from further away than the others. Russian
    /// rather than English on purpose: a <c>ResourceManager</c> in a test process falls back to the
    /// neutral text when a satellite assembly is missing, so asserting the exact Russian proves the
    /// satellite was built as well as that the translation says what it should.
    /// </remarks>
    [Fact]
    public void The_refusal_for_a_grant_from_another_host_tells_the_operator_it_is_still_wide()
    {
        using var _ = new CultureScope("ru");
        var text = DatabasesTestContext.RefusalText();

        Assert.Equal("Выдано для другого хоста", text.NameOf(GrantRefusalReason.HostIsNotLocalhost));
        Assert.Contains(
            "Широкий шаблон в имени остался.",
            text.AdviceOf(GrantRefusalReason.HostIsNotLocalhost),
            StringComparison.Ordinal);
    }

    /// <summary>Every refusal's advice says, in every language, that nothing was changed.</summary>
    /// <remarks>
    /// <para>
    /// The promise that must survive a copy edit. A refused row is one the panel did not touch, and an
    /// operator who believes the panel "handled" it will leave a wide grant standing. The clause is
    /// asserted per locale because there is no language in which the three sentences share a word.
    /// </para>
    /// <para>
    /// Its positive control is
    /// <see cref="The_unchanged_clause_sweep_does_not_match_a_repaired_rows_name"/>: the clause must be
    /// absent from text that is not a refusal advice, or the assertion would pass on anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_refusals_advice_says_in_every_language_that_nothing_was_changed()
    {
        var clauses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "Nothing was changed.",
            ["ru"] = "Ничего не изменено.",
            ["hy"] = "Ոչինչ չի փոփոխվել։",
        };

        Assert.Equal(Cultures.Length, clauses.Count);

        foreach (var culture in Cultures)
        {
            using var _ = new CultureScope(culture);
            var text = DatabasesTestContext.RefusalText();

            foreach (var reason in Enum.GetValues<GrantRefusalReason>())
            {
                Assert.Contains(clauses[culture], text.AdviceOf(reason), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>The unchanged-clause sweep would notice text that had lost the clause.</summary>
    /// <remarks>
    /// The positive control the sweep above owes. A refusal's NAME is the neighbouring string in the same
    /// resource file and carries no such clause, so a sweep that could match anything would fail here.
    /// </remarks>
    [Fact]
    public void The_unchanged_clause_sweep_does_not_match_a_repaired_rows_name()
    {
        var text = DatabasesTestContext.RefusalText();

        Assert.DoesNotContain(
            "Nothing was changed.",
            text.NameOf(GrantRefusalReason.HostIsNotLocalhost),
            StringComparison.Ordinal);
    }
}
