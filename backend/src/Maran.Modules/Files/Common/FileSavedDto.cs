namespace Maran.Modules.Files.Common;

/// <summary>What a text save answers.</summary>
/// <param name="SizeBytes">The size of the saved file in bytes.</param>
/// <param name="ModifiedAt">The file's new modification instant, to send as the next save's expected value.</param>
public sealed record FileSavedDto(ulong SizeBytes, DateTimeOffset ModifiedAt);
