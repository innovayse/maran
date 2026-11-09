namespace Maran.Packaging.Models;

/// <summary>The interface bundle a package carries, when it has a screen of its own.</summary>
/// <param name="Entry">The bundle's entry file, an entry under <c>spa/</c>.</param>
/// <param name="Integrity">
/// <c>sha384-</c> followed by the base64 SHA-384 of the entry file: the Subresource Integrity form, so
/// the browser can check the very bytes the signature covered. It must equal the entry file's pin.
/// </param>
/// <param name="Locales">
/// The locales the bundle translates, each with a <c>spa/locales/&lt;locale&gt;.json</c> entry; always
/// includes <c>en</c>, the fallback.
/// </param>
public sealed record IpmSpaInfo(string Entry, string Integrity, IReadOnlyList<string> Locales);
