namespace Maran.Modules.Dns.Queries.ListRecords;

/// <summary>
/// The public names the records list sorts by — the <c>sort</c> parameter's vocabulary, written once and read
/// by <see cref="ListRecordsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class ZoneRecordSortFields
{
    /// <summary>The record's owner name.</summary>
    public const string Name = "name";

    /// <summary>The record's type.</summary>
    public const string Type = "type";

    /// <summary>The record's time to live.</summary>
    public const string Ttl = "ttl";
}
