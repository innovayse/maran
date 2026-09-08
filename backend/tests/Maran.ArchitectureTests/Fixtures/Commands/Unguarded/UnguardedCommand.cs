using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.ArchitectureTests.Fixtures.Commands.Unguarded;

/// <summary>
/// The deliberate violation <see cref="BoundCommandGuardTests.The_rule_names_a_planted_violation"/>
/// runs the rule over: a command whose server-established members are open to the request body.
/// </summary>
/// <remarks>
/// It carries all four shapes of the mistake at once, so the control proves the rule detects each
/// and reports it in the words a reader has to act on: a member with NO guard, a member with only
/// the guard that does not work on a JSON body, a member carrying both PROPERTY-target attributes
/// and therefore still open to the query string, and a route id. It is never composed by the panel —
/// it lives in the test assembly, which <see cref="Maran.Host.Modules.ModuleRegistry"/> does not
/// know about — so nothing here is reachable over HTTP.
/// </remarks>
/// <param name="Name">A member the caller legitimately supplies; the rule must not object to it.</param>
/// <param name="WidgetId">A route id, bound from the body. The violation.</param>
/// <param name="IpAddress">Server-established, entirely unguarded. The violation.</param>
/// <param name="UserAgent">
/// Server-established, carrying only <see cref="BindNeverAttribute"/> — which does nothing to a
/// JSON body. This member is why the rule demands both attributes rather than either.
/// </param>
/// <param name="UserId">
/// Server-established, carrying <c>[property: JsonIgnore][property: BindNever]</c> and nothing
/// else — the shape the rules once prescribed. Both attributes land on the generated PROPERTY, and
/// a positional record bound from a query string is bound through its CONSTRUCTOR, so this member
/// is still attacker-writable. It is planted here permanently because the rule was blind to
/// exactly this shape once and must never be again.
/// </param>
public sealed record UnguardedCommand(
    string Name,
    Guid WidgetId,
    string IpAddress,
    [property: BindNever] string UserAgent,
    [property: JsonIgnore][property: BindNever] Guid UserId);
