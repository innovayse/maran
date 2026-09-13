using System.Reflection;
using Maran.Host.Modules;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// Builds every module's <c>DbContext</c> far enough to read its MODEL, so architecture tests can
/// ask questions of the mapping itself rather than of the source that produced it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the model and not the source.</b> A global query filter can be registered in
/// <c>OnModelCreating</c>, in an <c>IEntityTypeConfiguration</c>, or on a base type, and a grep for
/// <c>HasQueryFilter</c> answers none of those correctly. <c>IEntityType.GetQueryFilter()</c> is the
/// thing the query pipeline itself consults, so it is what these tests consult.
/// </para>
/// <para>
/// <b>Why no database is needed.</b> Building a model neither opens a connection nor sends a
/// statement — the Npgsql provider is configured so the model is the REAL one the panel runs, with
/// its schemas, converters and column types, and the connection string is never used.
/// </para>
/// <para>
/// <b>Why the contexts come from <see cref="ModuleRegistry"/>.</b> The registry is the panel's own
/// list of composed modules, so a module added to it is covered here without anybody remembering to
/// extend a list; a module NOT in it is not loaded by the Host either, and a test that scanned more
/// widely would be asserting about code the panel does not run.
/// </para>
/// </remarks>
public static class ModuleDbContexts
{
    /// <summary>A connection string that is configured and never used.</summary>
    private const string UnusedConnectionString = "Host=architecture.invalid;Database=maran";

    /// <summary>Creates one instance of every <c>DbContext</c> the composed modules declare.</summary>
    /// <returns>One context per declared type, ready for <see cref="DbContext.Model"/> to be read.</returns>
    /// <exception cref="InvalidOperationException">
    /// A context declares a constructor dependency this fixture cannot supply. Thrown rather than
    /// skipped: a context quietly left out of the census is a context whose tenancy nobody checks,
    /// which is the exact failure these tests exist to prevent.
    /// </exception>
    public static IReadOnlyList<DbContext> CreateAll()
    {
        return ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(type => { return type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract; })
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal)
            .Select(Create)
            .ToList();
    }

    /// <summary>Creates one context, supplying each constructor argument by its declared type.</summary>
    /// <param name="contextType">The context to construct.</param>
    /// <returns>The constructed context.</returns>
    private static DbContext Create(Type contextType)
    {
        var constructor = contextType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => { return Argument(contextType, parameter); })
            .ToArray();

        return (DbContext)constructor.Invoke(arguments);
    }

    /// <summary>Supplies one constructor argument, or refuses to guess.</summary>
    /// <param name="contextType">The context being constructed, named in the refusal.</param>
    /// <param name="parameter">The argument to supply.</param>
    /// <returns>The value to pass.</returns>
    private static object Argument(Type contextType, ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(DbContextOptions<>).MakeGenericType(contextType))
        {
            return Options(contextType);
        }

        if (parameter.ParameterType == typeof(ICurrentUser))
        {
            return new ArchitectureCurrentUser();
        }

        if (parameter.ParameterType == typeof(IEncryptionService))
        {
            return new ArchitectureEncryptionService();
        }

        throw new InvalidOperationException(
            $"{contextType.Name} takes a {parameter.ParameterType.Name} that Maran.ArchitectureTests cannot supply. "
            + "Add it to ModuleDbContexts.Argument — a context this fixture cannot build is a context "
            + "whose tenant scoping stops being checked.");
    }

    /// <summary>Builds the typed options a context's constructor expects.</summary>
    /// <param name="contextType">The context whose options type is needed.</param>
    /// <returns>Options carrying the real provider and an unused connection string.</returns>
    private static object Options(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;

        builder.UseNpgsql(UnusedConnectionString);

        // DeclaredOnly, because `Options` is declared on BOTH the generic builder (returning
        // DbContextOptions<T>, which the constructor wants) and its non-generic base (returning
        // DbContextOptions), and an unqualified lookup finds two and throws.
        var options = builderType.GetProperty(
            nameof(DbContextOptionsBuilder<DbContext>.Options),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

        return options.GetValue(builder)!;
    }
}
