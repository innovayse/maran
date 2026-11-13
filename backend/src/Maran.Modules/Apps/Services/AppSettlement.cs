namespace Maran.Modules.Apps.Services;

/// <summary>What one settling of the ledger changed, for the log line.</summary>
/// <param name="Found">Apps recorded because the host had them.</param>
/// <param name="Installed">Rows marked installed.</param>
/// <param name="Failed">Rows marked failed.</param>
/// <param name="Removed">Rows removed.</param>
public readonly record struct AppSettlement(int Found, int Installed, int Failed, int Removed);
