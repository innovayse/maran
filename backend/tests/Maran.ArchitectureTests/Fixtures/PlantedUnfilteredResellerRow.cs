namespace Maran.ArchitectureTests.Fixtures;

/// <summary>A planted entity a reseller owns, whose context does NOT filter it: the census's refusing control.</summary>
public sealed class PlantedUnfilteredResellerRow
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; set; }

    /// <summary>The reseller that owns the row, or null for the administrator's.</summary>
    public Guid? ResellerId { get; set; }
}
