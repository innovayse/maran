using System.Resources;
using Maran.Modules.Files.Jobs;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Services;
using Maran.Modules.Files.Validators;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files;

/// <summary>
/// The Files module's entry point (rules/csharp.md "Canonical backend layout"). Contributes the
/// module's controllers to the Host's routing, registers its audit journal, account gate and transfer
/// slots, and owns the panel's side of the customer file manager.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no database table, and that is the design rather than a gap.</b> A customer's files, and
/// the trash beside them, live in the account's home on the host, and only the agent may read or write
/// them. A listing is read live, so a panel copy would be a second answer to "what is in this folder"
/// that goes stale the first time the customer uploads a file over SFTP. Everything this module reads it
/// reads through the agent, and everything it changes it changes there.
/// </para>
/// <para>
/// The tenant boundary therefore cannot be a query filter: it is the resolution of an account id to a
/// system user name through <see cref="IAccountDirectory"/>, which answers null for an account the caller
/// does not own (<see cref="FilesAccountGate"/>).
/// </para>
/// </remarks>
public sealed class FilesModule : IPanelModule
{
    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Files.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Files.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return FilesManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Scoped, because the journal reads the request's own ICurrentUser for the entry's actor.
        services.AddScoped<FilesAuditJournal>();

        // Scoped, because it reads the request's own tenant-scoped account directory and caller.
        services.AddScoped<FilesAccountGate>();

        // Scoped: it writes through the request's journal and resolves error text in the request's culture.
        services.AddScoped<FilesBatchRunner>();

        // Scoped like the handlers that call it; it holds no state of its own.
        services.AddScoped<FilesEntryLookup>();

        // Scoped, because the localizer it wraps resolves the request's own culture.
        services.AddScoped<FilesDisplayNames>();

        // The limits, validated at startup so a value that would silently disable a protection refuses
        // the boot (rules/csharp.md "Options validated at startup").
        services.AddOptions<FilesOptions>()
            .Bind(configuration.GetSection(FilesOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FilesOptions>, FilesOptionsValidator>();

        // Singleton: the count of transfers in flight has to be shared by every request.
        services.AddSingleton<FilesTransferSlots>();

        // Registration block for the trash-retention job (issue #169, mutation side): its handler and
        // the cadence it is requested on are registered here, beside the services above.

        // Scoped, because it journals through the request-scoped writer — resolved once per Wolverine
        // message, the same way the Backups module resolves its retention handler. The Host schedules
        // TrashRetentionRequested once a day; this is what runs when it arrives.
        services.AddScoped<TrashRetentionHandler>();

        // Scoped: it reads one entry back through the lookup after a create or a rename.
        services.AddScoped<FilesEntryReader>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(FilesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(FilesModule).Assembly));
    }
}
