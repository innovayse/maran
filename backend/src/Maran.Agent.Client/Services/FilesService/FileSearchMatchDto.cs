namespace Maran.Agent.Client.Services.FilesService;

/// <summary>One entry a name search found.</summary>
/// <param name="Path">The entry's path relative to the account home, "/"-separated, without a leading "/".</param>
/// <param name="Entry">The entry itself; its name is the last component of <paramref name="Path"/>.</param>
public sealed record FileSearchMatchDto(string Path, FileEntryDto Entry);
