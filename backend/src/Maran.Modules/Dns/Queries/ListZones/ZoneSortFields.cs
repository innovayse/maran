namespace Maran.Modules.Dns.Queries.ListZones;

/// <summary>
/// The public names the zones list sorts by — the <c>sort</c> parameter's vocabulary, written once and read by
/// <see cref="ListZonesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class ZoneSortFields
{
    /// <summary>The zone's apex name.</summary>
    public const string Name = "name";

    /// <summary>When the zone was created.</summary>
    public const string CreatedAt = "createdAt";
}
