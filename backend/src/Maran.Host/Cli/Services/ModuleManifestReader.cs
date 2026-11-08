using System.Reflection;
using Maran.Host.Cli.Models;
using Maran.Packaging.Constants;
using Maran.Packaging.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Maran.Host.Cli.Services;

/// <summary>
/// Reads a module's own <see cref="Manifest"/> out of its compiled assembly and states it as the manifest of
/// a package, so a package's manifest is never written by hand and cannot disagree with the module it
/// carries. Everything a package adds that the module cannot know (the hash of each entry, the interface
/// bundle) is filled in later by the package builder.
/// </summary>
/// <remarks>
/// <para>
/// <b>It runs the module's constructor.</b> Finding the manifest means creating the module, so this reads
/// only an assembly the caller just built from their own source. It is never pointed at a downloaded
/// package: a package is judged by the verification library, which loads nothing.
/// </para>
/// <para>
/// The migrations a package carries are the ones the assembly declares (<see cref="MigrationAttribute"/>),
/// and the range starts from nothing: a package always brings the whole chain, so installing it over an
/// older version is the same operation as installing it fresh.
/// </para>
/// </remarks>
public static class ModuleManifestReader
{
    /// <summary>Reads the manifest of the one module an assembly holds.</summary>
    /// <param name="assemblyPath">The path of the module assembly.</param>
    /// <returns>The package manifest, or why the assembly cannot be packaged.</returns>
    public static ModuleManifestReading Read(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        var loaded = Load(assemblyPath);

        if (loaded.Failure is not null)
        {
            return new ModuleManifestReading(null, loaded.Failure);
        }

        return Read(loaded.Types, Path.GetFileName(assemblyPath));
    }

    /// <summary>Reads the manifest of the one module among the types an assembly declares.</summary>
    /// <param name="types">The types the assembly declares.</param>
    /// <param name="assemblyFileName">The assembly's file name, which becomes the package's entry assembly.</param>
    /// <returns>The package manifest, or why the types cannot be packaged.</returns>
    public static ModuleManifestReading Read(IReadOnlyList<Type> types, string assemblyFileName)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyFileName);

        var modules = types
            .Where(type => { return typeof(IPanelModule).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract; })
            .ToList();

        if (modules.Count != 1)
        {
            return Failed($"{assemblyFileName} holds {modules.Count} modules; a package carries exactly one.");
        }

        var created = Create(modules[0]);

        if (created.Failure is not null)
        {
            return new ModuleManifestReading(null, created.Failure);
        }

        var read = ReadManifest(created.Module!);

        if (read.Failure is not null)
        {
            return new ModuleManifestReading(null, read.Failure);
        }

        var manifest = read.Manifest!;

        if (manifest.Kind != ModuleKind.Packageable)
        {
            return Failed($"Module '{manifest.Id}' is {manifest.Kind}; a core module is part of the panel and is never packaged.");
        }

        var migrations = types
            .SelectMany(type => { return type.GetCustomAttributes<MigrationAttribute>(false); })
            .Select(attribute => { return attribute.Id; })
            .Order(StringComparer.Ordinal)
            .ToList();

        if (manifest.SchemaName is not null && migrations.Count == 0)
        {
            return Failed($"Module '{manifest.Id}' owns schema '{manifest.SchemaName}' but the assembly carries no migration.");
        }

        if (manifest.SchemaName is null && migrations.Count > 0)
        {
            return Failed($"Module '{manifest.Id}' declares no schema but the assembly carries {migrations.Count} migration(s).");
        }

        return new ModuleManifestReading(
            new IpmManifest(
                IpmLayout.CurrentFormatVersion,
                manifest.Id,
                manifest.DisplayNameKey,
                manifest.Version,
                manifest.Tier,
                manifest.Audience,
                manifest.MinPanelVersion,
                null,
                null,
                manifest.SchemaName,
                manifest.AgentCapabilities,
                manifest.Permissions,
                manifest.Menu,
                manifest.Routes,
                manifest.Dependencies,
                migrations.Count == 0 ? null : new IpmMigrationRange(null, migrations[^1]),
                assemblyFileName,
                null,
                null,
                []),
            null);
    }

    /// <summary>Loads the assembly and lists its types.</summary>
    /// <param name="assemblyPath">The path of the module assembly.</param>
    /// <returns>The types, or why the file is not a usable assembly.</returns>
    private static (IReadOnlyList<Type> Types, string? Failure) Load(string assemblyPath)
    {
        try
        {
            return (Assembly.LoadFrom(Path.GetFullPath(assemblyPath)).GetTypes(), null);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or ReflectionTypeLoadException)
        {
            return ([], $"{Path.GetFileName(assemblyPath)} cannot be read as a module assembly: {exception.GetType().Name}.");
        }
    }

    /// <summary>Creates the module the way the panel does, through its public parameterless constructor.</summary>
    /// <param name="moduleType">The module's type.</param>
    /// <returns>The module, or why it could not be created.</returns>
    private static (IPanelModule? Module, string? Failure) Create(Type moduleType)
    {
        try
        {
            return (Activator.CreateInstance(moduleType) as IPanelModule, null);
        }
        catch (Exception exception) when (exception is MissingMethodException or TargetInvocationException)
        {
            return (null, $"{moduleType.Name} could not be created through a public parameterless constructor: {exception.GetType().Name}.");
        }
    }

    /// <summary>Reads the manifest the created module publishes.</summary>
    /// <param name="module">The module the assembly holds.</param>
    /// <returns>The manifest, or a sentence saying the module's manifest cannot be read.</returns>
    /// <remarks>
    /// The getter is the module's own code and may throw or answer null. Either is the author's mistake in
    /// the assembly just built, so it is answered with a sentence and the failure exit code, never a stack trace.
    /// </remarks>
    private static (Manifest? Manifest, string? Failure) ReadManifest(IPanelModule module)
    {
        try
        {
            var manifest = module.Manifest;

            return manifest is null
                ? (null, $"{module.GetType().Name} publishes no manifest.")
                : (manifest, null);
        }
        catch (Exception exception)
        {
            // Deliberately broad, with the reason on the line: any exception from the module's getter
            // means the same to the operator, and only its type name is shown, never its text or trace.
            return (null, $"{module.GetType().Name} could not state its manifest: {exception.GetType().Name}.");
        }
    }

    /// <summary>States a failure.</summary>
    /// <param name="failure">The sentence for the operator.</param>
    /// <returns>A reading that carries only the failure.</returns>
    private static ModuleManifestReading Failed(string failure)
    {
        return new ModuleManifestReading(null, failure);
    }
}
