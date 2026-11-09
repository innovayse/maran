namespace Maran.Modules.Files.Common;

/// <summary>What extracting an archive answers.</summary>
/// <param name="EntryCount">How many entries the agent wrote.</param>
public sealed record FileExtractedDto(int EntryCount);
