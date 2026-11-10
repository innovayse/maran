using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// A named set of records an administrator offers to put into a new zone. This row belongs to the server and
/// to no customer, so it carries no account id and no tenant filter, and what authorises every path that
/// writes it is the <c>AdminOnly</c> policy of the templates controller; a customer is only ever told the
/// template's id and name, never its records.
/// </summary>
/// <remarks>
/// A template stores tokens, not a zone's data. Expanding it for a zone is <see cref="TemplateExpansionPolicy"/>,
/// and what it produces is weighed by the same record rules as a customer's own record.
/// </remarks>
public sealed class ZoneTemplate
{
    /// <summary>The template's records, owned by this entity and changed only through its methods.</summary>
    private readonly List<ZoneTemplateRecord> _records = [];

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The template's name as the administrator wrote it.</summary>
    public string Name { get; private set; }

    /// <summary>The name in the spelling two names are compared in, which a unique index holds.</summary>
    public string NameKey { get; private set; }

    /// <summary>What the template is for, or empty.</summary>
    public string Description { get; private set; }

    /// <summary>Whether the template ships with the panel. A built-in template can be edited but never deleted.</summary>
    public bool IsBuiltIn { get; private set; }

    /// <summary>The instant the template was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The template's records, in the order they were written; empty until they are loaded.</summary>
    public IReadOnlyList<ZoneTemplateRecord> Records
    {
        get
        {
            return _records.OrderBy(record =>
            {
                return record.Position;
            }).ToList();
        }
    }

    /// <summary>Creates a template with no records.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="name">The name, already passed through <see cref="TemplateNamePolicy.Normalize"/> and accepted.</param>
    /// <param name="description">The description, possibly empty.</param>
    /// <param name="isBuiltIn">Whether the template ships with the panel.</param>
    /// <param name="now">The creation instant, from the clock.</param>
    public ZoneTemplate(Guid id, string name, string description, bool isBuiltIn, DateTimeOffset now)
    {
        if (!TemplateNamePolicy.IsNameValid(name) || !TemplateNamePolicy.IsDescriptionValid(description))
        {
            throw new ArgumentException("A template is created from an accepted name and description.", nameof(name));
        }

        Id = id;
        Name = name;
        NameKey = TemplateNamePolicy.Key(name);
        Description = description;
        IsBuiltIn = isBuiltIn;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ZoneTemplate()
    {
        Name = string.Empty;
        NameKey = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Replaces the template whole: its name, its description and every record.</summary>
    /// <param name="name">The new name, normalised and accepted.</param>
    /// <param name="description">The new description, possibly empty.</param>
    /// <param name="records">The complete new record set, already accepted by the token and shape rules.</param>
    /// <param name="now">The change instant, from the clock.</param>
    public void Replace(string name, string description, IReadOnlyList<TemplateRecordDraft> records, DateTimeOffset now)
    {
        if (!TemplateNamePolicy.IsNameValid(name) || !TemplateNamePolicy.IsDescriptionValid(description))
        {
            throw new ArgumentException("A template is changed to an accepted name and description.", nameof(name));
        }

        Name = name;
        NameKey = TemplateNamePolicy.Key(name);
        Description = description;
        _records.Clear();
        for (var position = 0; position < records.Count; position++)
        {
            _records.Add(new ZoneTemplateRecord(Guid.NewGuid(), Id, position, records[position]));
        }

        UpdatedAt = now;
    }
}
