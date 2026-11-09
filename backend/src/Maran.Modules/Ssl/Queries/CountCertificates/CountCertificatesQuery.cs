namespace Maran.Modules.Ssl.Queries.CountCertificates;

/// <summary>
/// Counts the site certificates the caller may see, for the first-run checklist that asks whether
/// any exists rather than which. The panel's own certificate is a different record with its own
/// page and is not counted.
/// </summary>
/// <remarks>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator.
/// </remarks>
public sealed record CountCertificatesQuery;
