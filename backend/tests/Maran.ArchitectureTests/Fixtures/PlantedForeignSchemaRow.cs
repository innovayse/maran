namespace Maran.ArchitectureTests.Fixtures;

/// <summary>The one entity of <see cref="PlantedForeignSchemaContext"/>, mapped into a schema that is not its module's.</summary>
public sealed class PlantedForeignSchemaRow
{
    /// <summary>Gets or sets the key, which is all the row needs to be a mapped entity.</summary>
    public Guid Id { get; set; }
}
