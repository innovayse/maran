namespace Maran.Modules.Files.Queries.GetFilesOptions;

/// <summary>
/// Reads the limits and choices the file manager's screens are drawn from: how large an upload, an
/// edit and a download may be, how many entries a batch may hold, how long the trash keeps what it
/// is given, what an extraction is bounded by and which archive formats exist.
/// </summary>
/// <remarks>
/// Reference data (rules/architecture.md "The backend owns the data, the SPA renders it"): the same
/// for every caller, so it names no account, carries no tenant and discloses nothing about any
/// account. The limits are advice to the interface; every route enforces them again.
/// </remarks>
public sealed record GetFilesOptionsQuery;
