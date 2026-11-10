namespace Maran.Modules.Dns.Queries.ListZoneTemplates;

/// <summary>
/// The public names the templates list sorts by — the <c>sort</c> parameter's vocabulary, written once and read by
/// <see cref="ListZoneTemplatesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class ZoneTemplateSortFields
{
    /// <summary>The template's name.</summary>
    public const string Name = "name";

    /// <summary>When the template last changed.</summary>
    public const string UpdatedAt = "updatedAt";
}
