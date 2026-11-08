namespace Maran.Packaging.Tests.Builders;

/// <summary>An entry the fixture builder writes verbatim, with no pin and no check.</summary>
/// <param name="Name">The entry name.</param>
/// <param name="Bytes">The content.</param>
/// <param name="ExternalAttributes">The ZIP external attributes, or null for the writer's default.</param>
public sealed record IpmRawEntry(string Name, byte[] Bytes, int? ExternalAttributes);
