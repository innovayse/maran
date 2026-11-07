using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// What a personal-data export will contain, decided and journalled before a byte is streamed: the
/// sections every module's provider answered, and how many journal rows the stream may write.
/// </summary>
/// <param name="Subject">The user or account the export is about.</param>
/// <param name="Sections">Every provider's answer, in provider order; allow-listed fields only.</param>
/// <param name="AuditRowCount">
/// The number of journal rows about the subject when the plan was made. It is the BOUND of the stream,
/// not an estimate: the writer stops after this many rows, so entries written while the response is
/// being produced cannot push the export past the cap that was checked.
/// </param>
/// <param name="FileName">
/// The download's file name, built from the kind and the clock only; no caller-supplied text reaches
/// a response header.
/// </param>
public sealed record PersonalDataExportPlan(
    PersonalDataSubject Subject,
    IReadOnlyList<PersonalDataSection> Sections,
    long AuditRowCount,
    string FileName);
