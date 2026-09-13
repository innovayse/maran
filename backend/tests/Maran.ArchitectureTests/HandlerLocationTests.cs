using System.Reflection;
using Maran.Host.Modules;

namespace Maran.ArchitectureTests;

/// <summary>
/// Keeps message handlers where the panel's explicit module list can account for them, by making
/// the one place Wolverine still scans on its own contain nothing to find.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is actually being defended.</b> rules/architecture.md says handlers come from the module
/// registry and never from assembly scanning, and <c>MessagingExtensions</c> honours that by naming
/// each module assembly. What it CANNOT do is switch the default off:
/// <c>Discovery.DisableConventionalDiscovery()</c> disables scanning outright in Wolverine 6,
/// including the assemblies named explicitly, and turning it on took 201 of 233 integration tests
/// down. So Wolverine keeps scanning one assembly nobody listed — the entry assembly,
/// <c>Maran.Host</c>.
/// </para>
/// <para>
/// <b>So the guarantee is moved rather than dropped.</b> A handler in <c>Maran.Host</c> would become
/// a live message route that appears in no module's registration and belongs to no module's licence.
/// This test says there are none, which makes the entry-assembly scan a scan over nothing and the
/// registry the only real source. Wolverine's own generated wrappers live in
/// <c>Internal.Generated.WolverineHandlers</c> and are exactly what that scan is FOR, so they are
/// not what this asks about.
/// </para>
/// <para>
/// <b>Where a handler belongs instead.</b> In the module that owns the operation — the Host composes
/// modules and holds no business logic (rules/architecture.md), and a message it handled itself
/// would be business logic by another name.
/// </para>
/// </remarks>
public sealed class HandlerLocationTests
{
    /// <summary>Type-name suffixes Wolverine treats as a handler.</summary>
    private static readonly string[] HandlerSuffixes = ["Handler", "Consumer"];

    /// <summary>Method names Wolverine treats as a handler's entry point.</summary>
    private static readonly string[] HandlerMethods =
        ["Handle", "HandleAsync", "Handles", "Consume", "ConsumeAsync", "Consumes"];

    /// <summary>The Host declares no message handler of its own.</summary>
    [Fact]
    public void The_host_declares_no_message_handler()
    {
        var handlers = typeof(ModuleRegistry).Assembly.GetTypes()
            .Where(type => { return type.Namespace?.StartsWith("Maran.Host", StringComparison.Ordinal) == true; })
            .Where(IsWolverineHandler)
            .Select(type => { return type.FullName ?? type.Name; })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            handlers.Count == 0,
            "Maran.Host declares message handlers, and Wolverine scans the entry assembly on its own, so "
            + $"these are live routes no module registered and no licence covers: {string.Join(", ", handlers)}. "
            + "Move each into the module that owns the operation (rules/architecture.md).");
    }

    /// <summary>The probe recognises a handler when it is shown one.</summary>
    /// <remarks>
    /// The positive control. The assertion above is satisfied by an empty answer, so a predicate
    /// that had stopped recognising handlers — a convention Wolverine changed, a reflection flag
    /// that stopped matching — would pass it while the rule went unenforced. Module assemblies are
    /// full of real handlers; this requires the probe to find them.
    /// </remarks>
    [Fact]
    public void The_probe_recognises_the_handlers_the_modules_declare()
    {
        var found = ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Count(IsWolverineHandler);

        Assert.True(found >= 20, $"The handler probe found only {found} handlers across every module");
    }

    /// <summary>Whether Wolverine's conventions would treat a type as a message handler.</summary>
    /// <param name="type">The type to judge.</param>
    /// <returns><c>true</c> when the type is named and shaped like a handler.</returns>
    private static bool IsWolverineHandler(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !type.IsPublic)
        {
            return false;
        }

        if (!HandlerSuffixes.Any(suffix => { return type.Name.EndsWith(suffix, StringComparison.Ordinal); }))
        {
            return false;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Any(method => { return HandlerMethods.Contains(method.Name, StringComparer.Ordinal); });
    }
}
