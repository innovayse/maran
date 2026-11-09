namespace Maran.Host.Dashboard.Frames;

/// <summary>The root filesystem part of a <c>metrics</c> event.</summary>
/// <param name="Used">Bytes occupied, or absent when the source did not record it.</param>
/// <param name="Total">Capacity, in bytes.</param>
public sealed record MetricsDiskDto(long? Used, long Total);
