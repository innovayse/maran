namespace Maran.ArchitectureTests;

/// <summary>
/// The resource keys one module's manifest asks the catalogue to localize, and what the module's own
/// <c>Resources/DisplayNames</c> triple holds for them.
/// </summary>
/// <param name="ModuleId">The module's id.</param>
/// <param name="Keys">
/// Its display-name key, every menu <c>LabelKey</c> and every route <c>TitleKey</c>, in manifest order.
/// </param>
/// <param name="English">The neutral file's entries, key to value.</param>
/// <param name="Russian">The <c>.ru</c> file's entries; empty when the file does not exist.</param>
/// <param name="Armenian">The <c>.hy</c> file's entries; empty when the file does not exist.</param>
public sealed record ModuleResourceSubject(
    string ModuleId,
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, string> English,
    IReadOnlyDictionary<string, string> Russian,
    IReadOnlyDictionary<string, string> Armenian);
