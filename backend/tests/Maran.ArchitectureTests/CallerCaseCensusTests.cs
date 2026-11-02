using Maran.SharedKernel.Callers;

namespace Maran.ArchitectureTests;

/// <summary>
/// Fails when the set of concrete <see cref="Caller"/> cases is not exactly the four the switches
/// over it were written for, so a fifth case cannot arrive without every such switch being reviewed.
/// </summary>
/// <remarks>
/// Every consumer that decides something by who is calling — the tenant query filters, the audit
/// journals, the directories — names the cases in a <c>switch</c> and ends it with a guard arm the
/// compiler requires. That guard is not a decision: a case added later would reach it and throw at
/// run time, or worse, be swallowed by a base-type arm. This census is the review prompt the compiler
/// cannot give: it turns "a case was added" into a red test whose message lists what changed.
/// </remarks>
public sealed class CallerCaseCensusTests
{
    /// <summary>The four cases every switch over a caller names, in alphabetical order.</summary>
    private static readonly string[] KnownCases =
    [
        nameof(AccountlessCustomerCaller),
        nameof(AdministratorCaller),
        nameof(AnonymousCaller),
        nameof(CustomerCaller),
    ];

    /// <summary>The concrete cases of <see cref="Caller"/> are exactly the four known ones.</summary>
    [Fact]
    public void The_caller_cases_are_exactly_the_four_the_switches_name()
    {
        var found = ConcreteCases(typeof(Caller));

        Assert.True(
            found.SequenceEqual(KnownCases, StringComparer.Ordinal),
            $"Caller now has the concrete cases [{string.Join(", ", found)}] but the switches over it name "
            + $"[{string.Join(", ", KnownCases)}]. Review every switch over a Caller (tenant filters, audit "
            + "journals, directories): the new case must be named in each, and a tenant filter must show it "
            + "no rows unless it owns them. Then update KnownCases.");
    }

    /// <summary>The probe reports the concrete leaves of a hierarchy and not its abstract middle.</summary>
    /// <remarks>
    /// The positive control. The assertion above is satisfied by a probe that has stopped seeing
    /// subtypes only if the known list were empty, but a probe that skipped a level of the hierarchy
    /// (the abstract principal base) would report a smaller set and pass a census that was wrong in
    /// the other direction. Here the probe is shown a hierarchy with a known shape.
    /// </remarks>
    [Fact]
    public void The_probe_finds_the_concrete_leaves_below_an_abstract_middle()
    {
        var found = ConcreteCases(typeof(PlantedRoot));

        Assert.Equal([nameof(PlantedLeafOne), nameof(PlantedLeafTwo), nameof(PlantedSibling)], found);
    }

    /// <summary>Names the non-abstract types that derive from a root, whatever their depth.</summary>
    /// <param name="root">The base type whose hierarchy is enumerated.</param>
    /// <returns>The case names, sorted ordinally.</returns>
    private static string[] ConcreteCases(Type root)
    {
        return root.Assembly.GetTypes()
            .Where(type => { return type != root && type.IsAssignableTo(root) && !type.IsAbstract; })
            .Select(type => { return type.Name; })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Root of the planted hierarchy the control probes.</summary>
    private abstract record PlantedRoot;

    /// <summary>An abstract middle level, which must not be counted as a case.</summary>
    private abstract record PlantedMiddle : PlantedRoot;

    /// <summary>First leaf below the abstract middle.</summary>
    private sealed record PlantedLeafOne : PlantedMiddle;

    /// <summary>Second leaf below the abstract middle.</summary>
    private sealed record PlantedLeafTwo : PlantedMiddle;

    /// <summary>A leaf directly below the root.</summary>
    private sealed record PlantedSibling : PlantedRoot;
}
