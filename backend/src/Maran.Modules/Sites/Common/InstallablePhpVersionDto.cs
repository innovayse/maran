namespace Maran.Modules.Sites.Common;

/// <summary>
/// Outward view of one PHP version this server can install — the reference data the operator's
/// install picker selects from, so a version is never typed
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <param name="Version">Two-component version as the packages name it, e.g. <c>8.3</c>.</param>
/// <param name="Installed">
/// <see langword="true"/> when the server already has this version. The picker marks it and does not
/// offer it for installation; the list still carries it so the operator sees the whole range.
/// </param>
public sealed record InstallablePhpVersionDto(string Version, bool Installed);
