namespace Maran.Modules.Dns.Queries.GetZoneTemplate;

/// <summary>Reads one zone template with its records. Administrator only.</summary>
/// <param name="TemplateId">The template to read, from the route.</param>
public sealed record GetZoneTemplateQuery(Guid TemplateId);
