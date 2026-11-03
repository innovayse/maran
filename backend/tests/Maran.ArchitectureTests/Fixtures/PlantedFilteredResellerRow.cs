namespace Maran.ArchitectureTests.Fixtures;

/// <summary>A planted entity a reseller owns, whose context DOES filter it: the census's accepting control.</summary>
public sealed class PlantedFilteredResellerRow
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; set; }

    /// <summary>The reseller that owns the row, or null for the administrator's.</summary>
    public Guid? ResellerId { get; set; }
}
