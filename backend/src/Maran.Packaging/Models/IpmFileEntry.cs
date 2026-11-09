namespace Maran.Packaging.Models;

/// <summary>One payload file the manifest pins.</summary>
/// <param name="Path">The entry name in the archive, forward slashes, under <c>lib/</c> or <c>spa/</c>, or <c>lib.enc</c>.</param>
/// <param name="Sha384">The SHA-384 of the entry's unpacked bytes, 96 lowercase hexadecimal characters.</param>
/// <param name="Size">The unpacked size in bytes.</param>
public sealed record IpmFileEntry(string Path, string Sha384, long Size);
