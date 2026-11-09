namespace Maran.Sdk.Contracts;

/// <summary>The root filesystem part of one live host reading.</summary>
/// <param name="UsedBytes">
/// Bytes the filesystem accounts as occupied (<c>df</c>'s <c>Used</c>), or <c>null</c> when the source
/// did not record it: stored samples written before that figure was kept carry only the capacity.
/// </param>
/// <param name="TotalBytes">Capacity of the filesystem, in bytes.</param>
public sealed record LiveDiskReading(long? UsedBytes, long TotalBytes);
