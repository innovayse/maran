using System.Reflection;
using System.Text.Json.Serialization;
using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every endpoint binds its CQRS command directly, so a command's members ARE the request contract.
/// This makes the two members a caller must never supply — what the server established about them,
/// and the ids in the URL — a property of the build rather than of a reviewer's attention.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hazard.</b> A command carries <c>IpAddress</c> and <c>UserAgent</c> because they are
/// "recorded on the session and in the journal" (<c>LoginCommand</c>'s own doc). Bound from the
/// body, every session row and every audit line on the sign-in endpoint would carry whatever the
/// attacker typed — on the record used to spot a break-in. A command carrying <c>UserId</c> takes it
/// from the caller's own access token; bound from the body, any authenticated caller could aim
/// <c>two-factor/disable</c> at somebody else's account. A command carrying a route id bound from
/// the body could be sent an id that disagrees with the URL, and which one wins is whichever the
/// handler happens to read.
/// </para>
/// <para>
/// <b>Three attributes on two targets, and why each of the three is load-bearing.</b> The guard is
/// spelled <c>[property: JsonIgnore][property: BindNever][BindNever]</c> and every piece closes a
/// different door.
/// </para>
/// <para>
/// <see cref="JsonIgnoreAttribute"/> closes the JSON body. Measured on a net9.0 MVC app: a body of
/// <c>{"ipAddress":"6.6.6.6"}</c> against a <c>[FromBody]</c> parameter whose <c>IpAddress</c>
/// property carried only <see cref="BindNeverAttribute"/> reached the handler as <c>6.6.6.6</c>.
/// <c>[BindNever]</c> is a MODEL-BINDING attribute, and a <c>[FromBody]</c> parameter never enters
/// the model-binding pipeline — it is deserialized by an input formatter calling
/// <c>System.Text.Json</c>, which has never heard of it.
/// </para>
/// <para>
/// <b>And the property target alone does NOT close the query string — this test asserted that it
/// did, and was wrong.</b> A positional record has no settable properties for the binder to fill;
/// MVC's complex-object binder builds it through its CONSTRUCTOR, and it looks for
/// <c>[BindNever]</c> on the CONSTRUCTOR PARAMETER. A <c>[property:]</c> target lands on the
/// generated property, which that path never consults. Measured on a net9.0 MVC app: the shipped
/// <c>LoginCommand</c> shape, guarded with <c>[property: JsonIgnore][property: BindNever]</c> and
/// bound <c>[FromQuery]</c>, returned <c>ip=[6.6.6.6] ua=[ATTACKER-UA]</c>. The same record with the
/// bare parameter-target <c>[BindNever]</c> added returned both members empty. So the parameter
/// target is what actually closes the query string, the form and the route; the property target is
/// kept beside it because it is what a non-positional command — one with settable properties, bound
/// through the property path — needs, and because a command's shape may change without its guard
/// being re-read.
/// </para>
/// <para>
/// The three cover disjoint pipelines, so this test demands all three rather than picking a
/// favourite, and it reads the constructor parameter directly rather than the property, because
/// reading the property is precisely the mistake that let the query-string hole ship: with the
/// parameter-target attribute stripped from a live <c>[FromQuery]</c> command, the whole suite
/// stayed green on a provably spoofable endpoint.
/// </para>
/// <para>
/// <b>What this test cannot see, said plainly.</b> It proves a guarded member cannot be FILLED by a
/// caller. It cannot prove the action then STAMPS it — a method body is not visible to reflection —
/// so a forgotten <c>with</c> expression shows up as an empty address in the journal rather than as
/// a failure here. That is a data-loss defect, loud in the endpoint tests that assert the recorded
/// address, and it is strictly the lesser of the two failures: this test converts a spoofing bug
/// into a blank-field bug and no further.
/// </para>
/// <para>
/// <b>It asks the composed panel, not a list.</b> The census walks <see cref="ModuleRegistry.All"/>,
/// so a module added to the panel is covered without anybody remembering to extend anything, and a
/// module the Host does not load is not asserted about.
/// </para>
/// </remarks>
public sealed class BoundCommandGuardTests
{
    /// <summary>
    /// Members a command may carry that the SERVER establishes — never the caller — regardless of
    /// which endpoint binds the command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first two come from the connection and its headers (<c>ClientIpAddress</c>,
    /// <c>CallerUserAgent</c> on <c>BaseApiController</c>). <c>UserId</c> comes from the caller's own
    /// access token (<c>CurrentUser.UserId</c>) and is the most dangerous of the three: bound from a
    /// body it would let any authenticated caller aim an account-changing command at somebody else.
    /// Route ids are NOT listed here — they are discovered per action from the route template,
    /// because an id is only server-established on the endpoint whose URL carries it.
    /// </para>
    /// <para>
    /// <b><c>AccountId</c> is deliberately NOT here, and the first draft had it.</b> That draft
    /// failed on seven live endpoints — <c>SitesController.CreateAsync</c>,
    /// <c>DatabasesController.CreateAsync</c>, <c>SftpUsersController.CreateAsync</c>, the three
    /// <c>CronEntriesController</c> writes and <c>CronEnvironmentController.SetAsync</c> — and every
    /// one of them was RIGHT: this is an administrator's panel, and an administrator creating a site
    /// FOR an account names that account in the body. There is no server-side value to stamp over it
    /// with. Which accounts a caller may name is an AUTHORIZATION question, answered by the module's
    /// permission policy and by the tenant query filter <see cref="TenantScopeTests"/> enforces —
    /// never by model binding, which cannot tell an administrator from a customer. Listing it here
    /// would have made this test demand a change that breaks those endpoints.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ServerEstablishedMembers =
        new HashSet<string>(StringComparer.Ordinal) { "IpAddress", "UserAgent", "UserId" };

    /// <summary>
    /// No action of any composed module binds a command that leaves a server-established member
    /// open to a caller.
    /// </summary>
    [Fact]
    public void A_bound_command_never_exposes_a_server_established_member()
    {
        var controllers = ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(IsController)
            .ToList();

        var violations = FindViolations(controllers);

        Assert.True(
            violations.Count == 0,
            "A command bound at an endpoint exposes a member the server establishes to a caller. "
            + "Mark it [property: JsonIgnore][property: BindNever][BindNever] on the record parameter "
            + "— the bare [BindNever] is the one that closes the query string, the form and the "
            + "route, because a positional record is bound through its constructor — and stamp it in "
            + "the action with a `with` expression (rules/csharp.md "
            + "\"Server-established members of a command\"):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The positive control: the same rule, run over a planted violation, FAILS and names it.
    /// </summary>
    /// <remarks>
    /// Without this the test above is satisfied by an empty answer — it would pass loudest on the
    /// day the census stopped finding controllers at all (rules/README.md "Mechanical enforcement":
    /// every architecture test carries a positive control). <see cref="UnguardedCommandController"/>
    /// is the deliberate violation, and it lives in <c>Fixtures/</c> where the panel never composes
    /// it.
    /// </remarks>
    [Fact]
    public void The_rule_names_a_planted_violation()
    {
        var violations = FindViolations(new[] { typeof(UnguardedCommandController) });

        Assert.Equal(
            new[]
            {
                "UnguardedCommandController.SpoofableAsync binds UnguardedCommand.IpAddress "
                + "(server-established) — missing [JsonIgnore], missing [property: BindNever], "
                + "missing [BindNever] on the constructor parameter",
                "UnguardedCommandController.SpoofableAsync binds UnguardedCommand.UserAgent "
                + "(server-established) — missing [JsonIgnore], missing [BindNever] on the "
                + "constructor parameter",
                "UnguardedCommandController.SpoofableAsync binds UnguardedCommand.UserId "
                + "(server-established) — missing [BindNever] on the constructor parameter",
                "UnguardedCommandController.SpoofableAsync binds UnguardedCommand.WidgetId "
                + "(route parameter) — missing [JsonIgnore], missing [property: BindNever], "
                + "missing [BindNever] on the constructor parameter",
            },
            violations);
    }

    /// <summary>
    /// The staleness guard: the census really does reach the panel's bound commands, so the rule
    /// above is passing because the tree is clean and not because it looked at nothing.
    /// </summary>
    /// <remarks>
    /// The floor is the eight Identity endpoints that bind a command directly. It is a floor and not
    /// an equality: every module converts to this pattern, so the number only grows, and a test that
    /// had to be edited on each conversion would be edited without being read.
    /// </remarks>
    [Fact]
    public void The_census_finds_the_endpoints_that_bind_a_command()
    {
        var bound = ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(IsController)
            .SelectMany(controller => { return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance); })
            .SelectMany(action => { return action.GetParameters(); })
            .Count(IsBoundCommand);

        Assert.True(
            bound >= 8,
            $"The census found {bound} command-binding action parameters; it must find at least the "
            + "eight Identity endpoints. A census that finds none makes every rule above vacuous.");
    }

    /// <summary>Runs the rule over a set of controller types.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <returns>One ordered line per violation, naming the action, the command and the member.</returns>
    private static List<string> FindViolations(IEnumerable<Type> controllers)
    {
        var violations = new List<string>();

        foreach (var controller in controllers.OrderBy(type => { return type.Name; }, StringComparer.Ordinal))
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => { return method.DeclaringType == controller; })
                .OrderBy(method => { return method.Name; }, StringComparer.Ordinal);

            foreach (var action in actions)
            {
                var routeTokens = RouteTokensOf(controller, action);

                foreach (var parameter in action.GetParameters().Where(IsBoundCommand))
                {
                    violations.AddRange(ViolationsOf(controller, action, parameter, routeTokens));
                }
            }
        }

        return violations;
    }

    /// <summary>Checks one bound command parameter, member by member.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action binding the command.</param>
    /// <param name="parameter">The command parameter.</param>
    /// <param name="routeTokens">The tokens in the action's route template.</param>
    /// <returns>One line per unguarded member.</returns>
    private static List<string> ViolationsOf(
        Type controller,
        MethodInfo action,
        ParameterInfo parameter,
        HashSet<string> routeTokens)
    {
        var lines = new List<string>();

        var properties = parameter.ParameterType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => { return property.Name; }, StringComparer.Ordinal);

        // Ordered by name rather than by declaration: reflection makes no promise about the order it
        // returns properties in, and a failure message whose line order moves between runs is a
        // failure message nobody diffs.
        foreach (var property in properties)
        {
            var isServerEstablished = ServerEstablishedMembers.Contains(property.Name);
            var isRouteParameter = routeTokens.Contains(property.Name);
            if (!isServerEstablished && !isRouteParameter)
            {
                continue;
            }

            var missing = new List<string>();
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            {
                missing.Add("missing [JsonIgnore]");
            }

            if (property.GetCustomAttribute<BindNeverAttribute>() is null)
            {
                missing.Add("missing [property: BindNever]");
            }

            // The one that actually closes the query string. Read from the CONSTRUCTOR parameter,
            // never from the property: a positional record is bound through its constructor, so a
            // [property:] target is invisible to that path and a check that reads the property
            // reports a spoofable command as clean.
            var constructorParameter = BindingParameterOf(parameter.ParameterType, property);
            if (constructorParameter is not null
                && constructorParameter.GetCustomAttribute<BindNeverAttribute>() is null)
            {
                missing.Add("missing [BindNever] on the constructor parameter");
            }

            if (missing.Count == 0)
            {
                continue;
            }

            var reason = isServerEstablished ? "server-established" : "route parameter";
            lines.Add(
                $"{controller.Name}.{action.Name} binds {parameter.ParameterType.Name}.{property.Name} "
                + $"({reason}) — {string.Join(", ", missing)}");
        }

        return lines;
    }

    /// <summary>
    /// The accept-side control: the same rule, run over a fully guarded command, names nothing.
    /// </summary>
    /// <remarks>
    /// A gate that refuses everything passes every test that only ever hands it broken input
    /// (rules/testing.md: a refusing gate needs an inverse control). The panel's own tree is not
    /// that control — a tree that went wholesale wrong would take the control with it — so
    /// <see cref="GuardedCommandController"/> binds a correctly guarded command from the QUERY
    /// STRING, the pipeline this rule was blind to, and the rule must have nothing to say about it.
    /// </remarks>
    [Fact]
    public void The_rule_accepts_a_correctly_guarded_command()
    {
        var violations = FindViolations(new[] { typeof(GuardedCommandController) });

        Assert.Empty(violations);
    }

    /// <summary>
    /// Finds the primary-constructor parameter a bound member is filled through, if there is one.
    /// </summary>
    /// <remarks>
    /// MVC's complex-object binder constructs a type with no parameterless constructor through its
    /// single public constructor, matching parameters to values by name; a record's positional
    /// members are exactly that. A type with a parameterless constructor is filled through its
    /// property setters instead, and for it there is no parameter to guard — this returns
    /// <c>null</c> and the property-target check stands alone, which is correct for that shape.
    /// </remarks>
    /// <param name="commandType">The bound command type.</param>
    /// <param name="property">The member being checked.</param>
    /// <returns>The matching constructor parameter, or <c>null</c> when the type is not constructed.</returns>
    private static ParameterInfo? BindingParameterOf(Type commandType, PropertyInfo property)
    {
        var constructors = commandType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length != 1)
        {
            return null;
        }

        return constructors[0]
            .GetParameters()
            .FirstOrDefault(candidate =>
            {
                return string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase)
                    && candidate.ParameterType == property.PropertyType;
            });
    }

    /// <summary>Whether a type is a module controller the panel composes.</summary>
    /// <param name="type">The type to test.</param>
    /// <returns><c>true</c> for a concrete <see cref="ControllerBase"/> subclass.</returns>
    private static bool IsController(Type type)
    {
        return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract;
    }

    /// <summary>
    /// Whether an action parameter is a CQRS command the endpoint binds.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the parameter TYPE living in a <c>Commands.</c> namespace rather than on
    /// a <c>[FromBody]</c> attribute. Under <c>[ApiController]</c> a complex parameter is bound from
    /// the body with or without the attribute, so keying on the attribute would let an endpoint
    /// escape the rule by dropping it — the exact move this test must not reward.
    /// </remarks>
    /// <param name="parameter">The action parameter.</param>
    /// <returns><c>true</c> when the parameter is a command type.</returns>
    private static bool IsBoundCommand(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        return type.IsClass
            && type.Namespace is { } declared
            && declared.Contains(".Commands.", StringComparison.Ordinal)
            && type.Name.EndsWith("Command", StringComparison.Ordinal);
    }

    /// <summary>Reads the tokens of an action's route template, controller prefix included.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action.</param>
    /// <returns>The token names, e.g. <c>id</c> from <c>{id:guid}</c>, compared case-insensitively.</returns>
    private static HashSet<string> RouteTokensOf(Type controller, MethodInfo action)
    {
        var templates = new List<string>();

        foreach (var route in controller.GetCustomAttributes<RouteAttribute>())
        {
            templates.Add(route.Template);
        }

        foreach (var route in action.GetCustomAttributes().OfType<IRouteTemplateProvider>())
        {
            if (route.Template is { } template)
            {
                templates.Add(template);
            }
        }

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in templates.SelectMany(template => { return template.Split('/'); }))
        {
            if (!segment.StartsWith('{') || !segment.EndsWith('}'))
            {
                continue;
            }

            var name = segment.Trim('{', '}', '?').Split(':')[0].Split('=')[0];
            if (name.Length > 0)
            {
                tokens.Add(name);
            }
        }

        return tokens;
    }
}
