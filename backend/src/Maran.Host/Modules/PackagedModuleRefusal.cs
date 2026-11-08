using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Host.Modules;

/// <summary>
/// A module that arrived as a package and was NOT composed, and the one reason it was turned away.
/// The panel started without it; this is how an administrator learns why.
/// </summary>
/// <param name="Manifest">
/// What the module declared about itself, or null when its manifest could not be read at all (the reason is
/// then <see cref="Maran.Sdk.Modules.ModuleRefusals.ManifestUnreadable"/>). Untrusted: the module was refused, and nothing in it has been
/// verified beyond what the refusal says, so a consumer treats every member as data a stranger wrote.
/// </param>
/// <param name="Reason">
/// The refusal as a coded <see cref="Error"/> from the Sdk's <c>ErrorMessages</c> table, so it renders as
/// a sentence in the administrator's language and carries no exception text, path or tool output.
/// </param>
public sealed record PackagedModuleRefusal(Manifest? Manifest, Error Reason);
