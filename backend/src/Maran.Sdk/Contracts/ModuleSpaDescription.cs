namespace Maran.Sdk.Contracts;

/// <summary>
/// What a packaged module's screen bundle is, as the window that holds the verified package reports
/// it. The Host serves the bundle from this and from nothing the package supplies at run time: the
/// version it names is the one that is composed, the entry is a plain file name under the package's
/// <c>spa/</c> folder, and the integrity value was computed from the signed package contents.
/// </summary>
/// <param name="Version">The module version the bundle belongs to; the catalogue frames it only when it equals the composed version.</param>
/// <param name="Entry">The entry script's plain file name under <c>spa/</c> (for example <c>entry.js</c>), never a path.</param>
/// <param name="Integrity">The Subresource Integrity value of the entry file, <c>sha384-</c> followed by its base64 digest.</param>
/// <param name="Locales">The locales the bundle translates its own texts into; always includes <c>en</c>.</param>
public sealed record ModuleSpaDescription(string Version, string Entry, string Integrity, IReadOnlyList<string> Locales);
