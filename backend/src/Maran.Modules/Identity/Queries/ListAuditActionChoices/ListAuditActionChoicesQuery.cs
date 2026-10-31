namespace Maran.Modules.Identity.Queries.ListAuditActionChoices;

/// <summary>
/// Lists the actions the audit journal holds, for the journal's action filter: the reference data the
/// filter selects from, produced by the panel rather than typed by the operator
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The actions that OCCURRED, not every action the panel could record: a filter offering an action
/// that matches nothing is a choice that can only answer empty, and an action a marketplace module
/// recorded is offered as soon as it is in the journal, which a list compiled into this build could
/// not do.
/// </remarks>
public sealed record ListAuditActionChoicesQuery;
