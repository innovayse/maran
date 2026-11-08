namespace Maran.Modules.Files.Common;

/// <summary>What an upload answers.</summary>
/// <param name="SizeBytes">The number of bytes the panel received and the agent stored.</param>
public sealed record FileUploadedDto(ulong SizeBytes);
