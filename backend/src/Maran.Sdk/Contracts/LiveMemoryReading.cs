namespace Maran.Sdk.Contracts;

/// <summary>The memory part of one live host reading.</summary>
/// <param name="UsedBytes">Memory in use, in bytes.</param>
/// <param name="TotalBytes">Installed memory, in bytes.</param>
public sealed record LiveMemoryReading(long UsedBytes, long TotalBytes);
