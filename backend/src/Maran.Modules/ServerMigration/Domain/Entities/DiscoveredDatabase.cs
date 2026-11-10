using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>One database found on a remote server that belongs to a <see cref="DiscoveredAccount"/>, which creates it.</summary>
public sealed class DiscoveredDatabase
{
    /// <summary>The longest database name kept.</summary>
    public const int NameMaxLength = 64;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns the database.</summary>
    public Guid DiscoveredAccountId { get; private set; }

    /// <summary>The database's name on the source.</summary>
    public string Name { get; private set; }

    /// <summary>The database's size; zero when <see cref="SizeKnown"/> is false.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>Whether the source could measure the size.</summary>
    public bool SizeKnown { get; private set; }

    /// <summary>Creates the row from what the source reported.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="discoveredAccountId">The owning account.</param>
    /// <param name="name">The name on the source; shortened to the column.</param>
    /// <param name="sizeBytes">The size, or null when not measured; not negative.</param>
    internal DiscoveredDatabase(Guid id, Guid discoveredAccountId, string name, long? sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (sizeBytes is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes.Value);
        }

        Id = id;
        DiscoveredAccountId = discoveredAccountId;
        Name = ColumnText.Fit(name, NameMaxLength);
        SizeKnown = sizeBytes is not null;
        SizeBytes = sizeBytes ?? 0;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DiscoveredDatabase()
    {
        Name = string.Empty;
    }
}
