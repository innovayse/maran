namespace Maran.Packaging.Tests.Builders;

/// <summary>A payload file as the fixture builder holds it.</summary>
/// <param name="Name">The entry name.</param>
/// <param name="Bytes">The bytes the manifest pins.</param>
/// <param name="Pinned">Whether the manifest lists it.</param>
/// <param name="Stored">Whether the archive stores it.</param>
public sealed record IpmPayloadFile(string Name, byte[] Bytes, bool Pinned, bool Stored)
{
    /// <summary>The bytes stored when they differ from the pinned ones; null when they are the same.</summary>
    public byte[]? StoredBytes { get; init; }
}
