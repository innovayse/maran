namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>What expanding a template for one zone produced: the records to create and the ones left out.</summary>
/// <param name="Drafts">The records to create, variables substituted, not yet shape-checked.</param>
/// <param name="Skipped">The template records left out because a variable they need had no value.</param>
public sealed record TemplateExpansion(
    IReadOnlyList<RecordDraft> Drafts,
    IReadOnlyList<SkippedTemplateRecord> Skipped);
