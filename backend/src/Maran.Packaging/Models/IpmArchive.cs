namespace Maran.Packaging.Models;

/// <summary>
/// An archive the reader accepted as safe to hold in memory: names contained, sizes and ratios within
/// the caps, nothing duplicated. It is NOT yet trusted. Nothing here has been written to disk, and
/// nothing in it is believed until the signature over <see cref="ManifestBytes"/> has been checked.
/// </summary>
/// <param name="ManifestBytes">The stored bytes of <c>ipm.json</c>, exactly as signed.</param>
/// <param name="SignatureBytes">The stored bytes of <c>ipm.sig</c>.</param>
/// <param name="Payload">Every other entry by name, unpacked.</param>
public sealed record IpmArchive(
    byte[] ManifestBytes,
    byte[] SignatureBytes,
    IReadOnlyDictionary<string, byte[]> Payload);
