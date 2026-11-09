using Maran.Sdk.Manifests.Enums;

namespace Maran.Sdk.Manifests;

/// <summary>
/// One rule a manifest broke: which module, which rule, and the value that broke it.
/// </summary>
/// <param name="ModuleId">The id of the module whose manifest broke the rule, as that manifest spells it.</param>
/// <param name="Kind">Which rule.</param>
/// <param name="Subject">
/// The offending value, so a refusal can name it: the permission, the dependency id, the menu key, the
/// version text. Empty only when the offending value is itself empty (a blank id or version).
/// </param>
public sealed record ManifestFinding(string ModuleId, ManifestFindingKind Kind, string Subject);
