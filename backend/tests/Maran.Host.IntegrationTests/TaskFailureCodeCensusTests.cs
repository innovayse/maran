using System.Globalization;
using System.Reflection;
using System.Resources;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Every code a task can fail with has a sentence in the composed panel, in every shipped language.
/// The backend-side replacement for the SPA's old task-sentence check (issue #272).
/// </summary>
/// <remarks>
/// <para>
/// <b>What "every code a task can fail with" is, measured rather than listed.</b> A task stores the
/// code of an <c>Error</c> some module raised, and a code is only ever written as
/// <c>nameof(ErrorMessages.X)</c> of some assembly's generated table (rules/csharp.md
/// "<c>ErrorMessages.resx</c> is where error codes are defined") — whichever file builds it, a
/// handler, a translator, an ACME client, a backup outcome. So the universe is the keys of every
/// <c>ErrorMessages</c> table embedded in the panel's shipped assemblies, read from the compiled
/// resources, not retyped here. A code built far from its task is in this universe the moment it
/// compiles, which is exactly what the old SPA check, a source scan over call sites, could not see.
/// </para>
/// <para>
/// <b>What is asserted is the composed pool, not the files.</b> Each code is asked of the
/// <see cref="IErrorTextProvider"/> the booted host composes from the tables its modules REGISTER,
/// in the culture a request would set — the very lookup <c>TaskFailureMessages</c> makes. An
/// assembly whose table exists but that never registers it with the pool is the failure this finds:
/// its codes would read as the generic sentence on every task.
/// </para>
/// <para>
/// UNOBSERVED HERE: a missing Russian or Armenian entry, because <see cref="ResourceManager"/>
/// answers it with the English text — <c>ResourceKeyParityTests</c> holds the three files of each
/// table to one key set; and a code written as a literal string rather than through <c>nameof</c>,
/// which no table defines and which reads as the generic sentence by design.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TaskFailureCodeCensusTests : IAsyncLifetime
{
    /// <summary>The suffix of the embedded resource that defines an assembly's error codes.</summary>
    private const string ErrorTableSuffix = ".Resources.ErrorMessages.resources";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TaskFailureCodeCensusTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Every shipped error code is described by the composed pool, in every shipped language.</summary>
    /// <param name="culture">The culture a request would set.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_code_a_task_can_fail_with_has_a_sentence_in_the_composed_panel(string culture)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var errorTexts = factory.Services.GetRequiredService<IErrorTextProvider>();
        var codes = ShippedErrorCodes();

        var undescribed = InCulture(culture, () =>
        {
            return codes
                .Where(code =>
                {
                    return !errorTexts.TryResolve(code, out _);
                })
                .ToList();
        });

        Assert.True(
            undescribed.Count == 0,
            $"These error codes have no sentence in the composed panel for '{culture}', so a task failing "
            + $"with one reads as the generic sentence: {string.Join(", ", undescribed)}");
    }

    /// <summary>
    /// The controls the census needs: its universe holds the codes built far from any task — the
    /// agent client's, a backup outcome's, the ACME client's — and the pool refuses a code no table
    /// defines, so a "described" answer is not given to everything.
    /// </summary>
    [Fact]
    public async Task The_census_sees_codes_built_far_from_any_task_and_the_pool_can_refuse()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var errorTexts = factory.Services.GetRequiredService<IErrorTextProvider>();
        var codes = ShippedErrorCodes();

        Assert.Contains("AgentUnavailable", codes);
        Assert.Contains("BackupTruncated", codes);
        Assert.Contains("AcmeAccountRejected", codes);
        Assert.Contains("TaskFailureUnexplained", codes);
        Assert.False(errorTexts.TryResolve("NoTableDefinesThisCode", out _));
    }

    /// <summary>Reads every error code the panel's shipped assemblies define.</summary>
    /// <returns>The codes, de-duplicated and ordinally sorted.</returns>
    private static List<string> ShippedErrorCodes()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // A native file matching the pattern is not a managed assembly and defines no code.
            }
        }

        var codes = new SortedSet<string>(StringComparer.Ordinal);
        var panelAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                return !assembly.IsDynamic
                    && assembly.GetName().Name?.StartsWith("Maran.", StringComparison.Ordinal) == true
                    && !assembly.GetName().Name!.EndsWith("Tests", StringComparison.Ordinal);
            });
        foreach (var assembly in panelAssemblies)
        {
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(ErrorTableSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new ResourceReader(stream);
                foreach (System.Collections.DictionaryEntry entry in reader)
                {
                    codes.Add((string)entry.Key);
                }
            }
        }

        return codes.ToList();
    }

    /// <summary>Runs <paramref name="read"/> in the UI culture a request would set, then restores it.</summary>
    /// <typeparam name="T">What the read returns.</typeparam>
    /// <param name="culture">The culture to read in.</param>
    /// <param name="read">The read.</param>
    /// <returns>What the read returned.</returns>
    private static T InCulture<T>(string culture, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
