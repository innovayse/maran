namespace Maran.Sdk.Contracts;

/// <summary>One thing the dry run of an import found, stated as a code and the subject it is about.</summary>
/// <remarks>
/// A finding is data: the backend localizes the sentence from <paramref name="Code"/> and the frontend only
/// renders it. The subject is a name the administrator can recognise (a domain, a database, a login) and never
/// a secret.
/// </remarks>
/// <param name="ModuleId">The module that found it.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Code">A machine-stable code, spelled like an error code and defined in the finding module's <c>ErrorMessages</c>.</param>
/// <param name="Subject">What it is about, as the administrator knows it.</param>
public sealed record MigrationFinding(string ModuleId, MigrationFindingSeverity Severity, string Code, string Subject);
