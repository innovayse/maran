using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Names the module's closed vocabularies in the caller's language, so the interface shows what the backend sends
/// and keeps no table of its own for them (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// A name is the display-name entry keyed by the enum's type name followed by the member name
/// (<c>RemoteServerStateReady</c>), so adding a member without its entry in all three languages is caught by the
/// test that walks every member, and a name needs no switch to keep in step with the enum.
/// </remarks>
public static class MigrationEnumNames
{
    /// <summary>The key of a server state's name.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(RemoteServerState state)
    {
        return nameof(RemoteServerState) + state;
    }

    /// <summary>The key of a source kind's name.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(RemoteSourceKind kind)
    {
        return nameof(RemoteSourceKind) + kind;
    }

    /// <summary>The key of a connection failure's name.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(RemoteFailureKind failure)
    {
        return nameof(RemoteFailureKind) + failure;
    }

    /// <summary>The key of a site backend's name.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(DiscoveredSiteBackend backend)
    {
        return nameof(DiscoveredSiteBackend) + backend;
    }

    /// <summary>Names a server state.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, RemoteServerState state)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(state)];
    }

    /// <summary>Names a source kind, when there is one.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="kind">The kind, or <c>null</c> when none has been found.</param>
    /// <returns>The kind in the caller's language, or <c>null</c> when there is none.</returns>
    public static string? Of(IStringLocalizer<DisplayNames> names, RemoteSourceKind? kind)
    {
        ArgumentNullException.ThrowIfNull(names);

        return kind is { } value ? names[KeyOf(value)].Value : null;
    }

    /// <summary>Names a connection failure, when there is one.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="failure">The failure, or <c>null</c> when the last attempt did not fail.</param>
    /// <returns>The failure in the caller's language, or <c>null</c> when there is none.</returns>
    public static string? Of(IStringLocalizer<DisplayNames> names, RemoteFailureKind? failure)
    {
        ArgumentNullException.ThrowIfNull(names);

        return failure is { } value ? names[KeyOf(value)].Value : null;
    }

    /// <summary>Names a site backend.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="backend">The backend.</param>
    /// <returns>The backend in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, DiscoveredSiteBackend backend)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(backend)];
    }

    /// <summary>The key of a job state's name.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(MigrationJobState state)
    {
        return nameof(MigrationJobState) + state;
    }

    /// <summary>The key of an account state's name.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(MigrationAccountState state)
    {
        return nameof(MigrationAccountState) + state;
    }

    /// <summary>The key of an import state's name.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(AccountImportState state)
    {
        return nameof(AccountImportState) + state;
    }

    /// <summary>The key of a pull grant state's name.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(ExportGrantState state)
    {
        return nameof(ExportGrantState) + state;
    }

    /// <summary>Names a job state.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, MigrationJobState state)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(state)];
    }

    /// <summary>Names an account state.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, MigrationAccountState state)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(state)];
    }

    /// <summary>Names an import state.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, AccountImportState state)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(state)];
    }

    /// <summary>Names a pull grant state.</summary>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state in the caller's language.</returns>
    public static string Of(IStringLocalizer<DisplayNames> names, ExportGrantState state)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names[KeyOf(state)];
    }
}
