namespace Maran.Modules.Firewall.Queries.ListPortPresets;

/// <summary>
/// Lists the port presets the rule screen offers beside the raw rule form: reference data produced
/// by the panel rather than hard-coded by the browser (rules/architecture.md "The backend owns the
/// data, the SPA renders it").
/// </summary>
/// <remarks>
/// A preset is advice: each rule in it is sent through the ordinary allow request, so the allow
/// command's validator stays the boundary and a preset can never open what a hand-typed rule could not.
/// </remarks>
public sealed record ListPortPresetsQuery;
