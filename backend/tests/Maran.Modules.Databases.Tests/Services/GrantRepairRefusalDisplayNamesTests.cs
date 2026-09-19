using Maran.Agent.V1;
using Maran.Modules.Databases.Tests.TestSupport;

namespace Maran.Modules.Databases.Tests.Services;

/// <summary>
/// Covers the two sentences a refused grant-table row carries, and — the part that makes this a fix
/// rather than a sample — that EVERY reason the agent can report has both of them.
/// </summary>
/// <remarks>
/// The set is walked, not sampled, because a reason with no entry does not fail, does not warn and
/// does not look like a defect from the backend: the resolver answers with the machine identifier, so
/// an operator reads <c>PartiallyOrUnfamiliarlyEscaped</c> where a sentence belongs, in all three
/// languages. The closed set is the agent contract's own enum, so a reason the agent gains fails here
/// rather than reaching a screen as a constant.
/// </remarks>
public sealed class GrantRepairRefusalDisplayNamesTests
{
    /// <summary>The three cultures this panel ships, neutral first.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>Every refusal the agent can report has a name and an action in every language.</summary>
    /// <remarks>
    /// Read through the REAL resource files by culture, so a missing satellite or an untranslated entry
    /// is visible here rather than in a browser. <c>Unspecified</c> is included deliberately: the agent
    /// never sends it, but a panel older than its agent receives values it cannot name, and this is the
    /// entry that keeps that case a sentence instead of a machine word.
    /// </remarks>
    [Fact]
    public void Every_refusal_the_agent_can_report_has_a_name_and_an_action_in_every_language()
    {
        var reasons = Enum.GetValues<GrantRepairRefusal>();
        Assert.NotEmpty(reasons);

        var problems = new List<string>();
        foreach (var culture in Cultures)
        {
            using var _ = new CultureScope(culture);
            var text = DatabasesTestContext.RefusalText();

            foreach (var reason in reasons)
            {
                var machine = reason.ToString();

                if (string.Equals(text.NameOf(machine), machine, StringComparison.Ordinal))
                {
                    problems.Add($"'{machine}' has no name in {culture}; the screen prints the identifier.");
                }

                if (string.Equals(text.AdviceOf(machine), machine, StringComparison.Ordinal))
                {
                    problems.Add($"'{machine}' has no advice in {culture}; the row states a verdict with no next step.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "A refusal reaches the operator's screen as a machine identifier (rules/vue.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    /// <summary>The walk reads the contract enum it claims to, and the resolver can miss.</summary>
    /// <remarks>
    /// The positive control the walk above owes, on the axis that can go blind (rules/testing.md). Two
    /// halves: the enumeration really contains the values this build ships — a reflection query that
    /// returned nothing would make the walk pass over nothing at all — and the resolver really answers
    /// with the identifier for a reason it has no entry for, which is the condition the walk detects. A
    /// resolver that answered with a sentence for every input would make the walk green forever.
    /// </remarks>
    [Fact]
    public void The_walk_reads_the_contract_enum_it_claims_to_and_the_resolver_can_miss()
    {
        var reasons = Enum.GetValues<GrantRepairRefusal>().Select(reason => { return reason.ToString(); }).ToList();

        Assert.Contains("HostIsNotLocalhost", reasons);
        Assert.Contains("NotThePanelsNaming", reasons);
        Assert.Contains("UnrecognisedPrivileges", reasons);
        Assert.Contains("PartiallyOrUnfamiliarlyEscaped", reasons);
        Assert.Contains("Unspecified", reasons);

        var text = DatabasesTestContext.RefusalText();
        Assert.Equal("AReasonNoAgentHasEverSent", text.NameOf("AReasonNoAgentHasEverSent"));
        Assert.Equal("AReasonNoAgentHasEverSent", text.AdviceOf("AReasonNoAgentHasEverSent"));
    }

    /// <summary>Nothing to name answers with nothing, and not with the bare resource prefix.</summary>
    /// <remarks>
    /// The failure this branch exists for is specific: handed an empty reason, an unguarded localizer
    /// looks up the key <c>GrantRepairRefusal</c>, finds nothing, and answers with that key — putting
    /// the words <c>GrantRepairRefusal</c> on the screen.
    /// </remarks>
    [Fact]
    public void Nothing_to_name_answers_with_nothing_and_not_with_the_bare_resource_prefix()
    {
        var text = DatabasesTestContext.RefusalText();

        Assert.Equal(string.Empty, text.NameOf(string.Empty));
        Assert.Equal(string.Empty, text.AdviceOf(string.Empty));
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

        Assert.Equal("Выдано для другого хоста", text.NameOf("HostIsNotLocalhost"));
        Assert.Contains(
            "Широкий шаблон в имени остался.",
            text.AdviceOf("HostIsNotLocalhost"),
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

            foreach (var reason in Enum.GetValues<GrantRepairRefusal>())
            {
                Assert.Contains(clauses[culture], text.AdviceOf(reason.ToString()), StringComparison.Ordinal);
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
            text.NameOf("HostIsNotLocalhost"),
            StringComparison.Ordinal);
    }
}
