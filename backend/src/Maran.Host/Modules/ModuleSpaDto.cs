namespace Maran.Host.Modules;

/// <summary>
/// How the shell reaches a packaged module's screen: the address of its frame, the integrity value of
/// its entry script and the locales its bundle translates. Present in the catalogue only for a module
/// the panel would frame right now (<see cref="ModuleFrameRule"/>).
/// </summary>
/// <param name="FrameUrl">
/// The frame's address, <c>/module-frames/{id}/{version}/</c>, built by the panel from the composed
/// module's own id and version and never a value the package supplies.
/// </param>
/// <param name="Integrity">The Subresource Integrity value of the entry script, <c>sha384-</c> and its base64 digest.</param>
/// <param name="Locales">The locales the bundle translates its own texts into; always includes <c>en</c>.</param>
public sealed record ModuleSpaDto(string FrameUrl, string Integrity, IReadOnlyList<string> Locales);
