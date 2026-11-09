namespace Maran.Modules.Files.Common;

/// <summary>What creating an archive answers.</summary>
/// <param name="SizeBytes">The size of the archive written, in bytes.</param>
public sealed record FileArchivedDto(ulong SizeBytes);
