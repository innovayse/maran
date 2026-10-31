namespace Maran.Modules.Identity.Common;

/// <summary>One action the audit journal can be filtered by, as the filter's choice list offers it.</summary>
/// <param name="Action">The machine-stable action, which is what the filter sends back.</param>
/// <param name="ActionName">
/// The action as an operator reads it, resolved for the request's culture; the machine action itself
/// for one this build carries no entry for.
/// </param>
public sealed record AuditActionChoiceDto(string Action, string ActionName);
