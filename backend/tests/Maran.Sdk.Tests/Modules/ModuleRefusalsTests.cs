using System.Reflection;
using Maran.Sdk.Modules;
using Maran.Sdk.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Sdk.Tests.Modules;

/// <summary>
/// The refusals a module can meet at composition are coded, distinct, and carry the kind the error-kind
/// census decided for their code, so the HTTP status an administrator's screen derives from one never
/// disagrees with the census.
/// </summary>
public sealed class ModuleRefusalsTests
{
    /// <summary>Every refusal carries the kind the census decided for its code.</summary>
    [Fact]
    public void Every_refusal_carries_the_kind_the_census_decided_for_its_code()
    {
        var refusals = All();

        Assert.True(refusals.Count >= 6, $"Only {refusals.Count} refusals were found; the reflection read nothing.");
        foreach (var refusal in refusals)
        {
            Assert.True(
                ExpectedErrorStatuses.Kinds.TryGetValue(refusal.Code, out var decided),
                $"{refusal.Code} has no decided kind in the census.");
            Assert.Equal(decided, refusal.Type);
        }
    }

    /// <summary>No two refusals share a code, so a coded failure names exactly one reason.</summary>
    [Fact]
    public void No_two_refusals_share_a_code()
    {
        var codes = All().Select(refusal => { return refusal.Code; }).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Reads every refusal the type declares.</summary>
    /// <returns>The refusals.</returns>
    private static List<Error> All()
    {
        return typeof(ModuleRefusals)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => { return field.FieldType == typeof(Error); })
            .Select(field => { return (Error)field.GetValue(null)!; })
            .ToList();
    }
}
