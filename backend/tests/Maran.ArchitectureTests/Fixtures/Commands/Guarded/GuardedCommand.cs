using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.ArchitectureTests.Fixtures.Commands.Guarded;

/// <summary>
/// The correctly guarded counterpart of <c>UnguardedCommand</c>: every server-established member and
/// the route id carry the full three-attribute guard the rules prescribe.
/// </summary>
/// <remarks>
/// It exists so <see cref="BoundCommandGuardTests.The_rule_accepts_a_correctly_guarded_command"/>
/// can feed the rule something it MUST accept. A gate mutated — or written — to refuse everything
/// passes every test that only ever hands it broken input (rules/testing.md: a refusing gate needs
/// an inverse control), and the panel's own tree is not that control, because a tree that went
/// wholesale wrong would take the control with it. Never composed by the panel.
/// </remarks>
/// <param name="Name">A member the caller legitimately supplies.</param>
/// <param name="WidgetId">The route id, guarded on both targets.</param>
/// <param name="IpAddress">Server-established, guarded on both targets.</param>
/// <param name="UserAgent">Server-established, guarded on both targets.</param>
/// <param name="UserId">Server-established, guarded on both targets.</param>
public sealed record GuardedCommand(
    string Name,
    [property: JsonIgnore][property: BindNever][BindNever] Guid WidgetId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress,
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId);
