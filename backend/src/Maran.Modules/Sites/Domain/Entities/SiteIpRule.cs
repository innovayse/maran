namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>One address or CIDR range of a site's IP rules. Created and removed with <see cref="SiteOptions"/> and by nothing else.</summary>
public sealed class SiteIpRule
{
    /// <summary>The rule's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The site whose options hold this rule.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The options this rule belongs to, as the relationship EF Core cascades on and the tenant filter reads through.</summary>
    public SiteOptions Options { get; private set; }

    /// <summary>The rule's place in the rendered order, from zero.</summary>
    public int Position { get; private set; }

    /// <summary>The address or range in the normal form <c>address/prefix</c>.</summary>
    public string Cidr { get; private set; }

    /// <summary>Creates a rule for <paramref name="options"/>.</summary>
    /// <param name="options">The options that hold the rule.</param>
    /// <param name="position">The rule's place in the rendered order.</param>
    /// <param name="cidr">The address or range, already normalised.</param>
    public SiteIpRule(SiteOptions options, int position, string cidr)
    {
        Id = Guid.NewGuid();
        Options = options;
        SiteId = options.SiteId;
        Position = position;
        Cidr = cidr;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteIpRule()
    {
        Options = null!;
        Cidr = string.Empty;
    }
}
