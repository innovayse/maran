namespace Maran.Sdk.Contracts;

/// <summary>A live grant: its id and the one export it opens.</summary>
public sealed record MigrationGrantAuthenticated : MigrationGrantAuthentication
{
    /// <summary>The id of the grant that authenticated.</summary>
    public Guid GrantId { get; }

    /// <summary>The export the grant opens, and no other.</summary>
    public Guid ExportId { get; }

    /// <summary>Creates the answer for a live grant.</summary>
    /// <param name="grantId">The id of the grant that authenticated.</param>
    /// <param name="exportId">The export the grant opens.</param>
    public MigrationGrantAuthenticated(Guid grantId, Guid exportId)
    {
        GrantId = grantId;
        ExportId = exportId;
    }
}
