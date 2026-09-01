namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// Lists the certificates the caller may see. Carries no filter of its own: what a caller may see is
/// decided by the tenant filter on <c>SslDbContext</c>, not by a parameter a caller could set.
/// </summary>
public sealed record ListCertificatesQuery;
