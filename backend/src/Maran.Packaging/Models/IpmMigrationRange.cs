namespace Maran.Packaging.Models;

/// <summary>The schema migrations a package carries for a module that owns a schema.</summary>
/// <param name="From">
/// The migration the schema must already be at for this package to apply, or null when the package also
/// creates the schema from nothing.
/// </param>
/// <param name="To">The last migration the package carries; the schema is at this one afterwards.</param>
public sealed record IpmMigrationRange(string? From, string To);
