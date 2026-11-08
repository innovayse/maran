namespace Maran.Host.Dashboard.Frames;

/// <summary>The memory part of a <c>metrics</c> event.</summary>
/// <param name="Used">Memory in use, in bytes.</param>
/// <param name="Total">Installed memory, in bytes.</param>
public sealed record MetricsMemoryDto(long Used, long Total);
