using System.Resources;
using Maran.Modules.Updates.Options;
using Maran.Modules.Updates.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates;

/// <summary>
/// The Updates module's entry point (rules/csharp.md "Canonical backend layout"). Lets an administrator
/// see the panel's release state and ask the root update service to run <c>maran update</c>, with the
/// progress in the task list (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>The panel never updates itself.</b> It writes a request marker into a directory the installer
/// built, and a root-owned systemd path unit starts <c>maran update --requested</c>. The module spawns
/// nothing, holds no root, calls no agent and makes no outbound call; the argument is
/// <c>docs/superpowers/notes/2026-11-07-panel-update-from-panel-threat-note.md</c> (second review
/// OUTSTANDING).
/// </para>
/// <para>
/// <b>No state of its own in the database.</b> The facts are root's files, read as untrusted text, and the
/// one open request is a one-line ledger file, so the module has no schema and no migration. The reaper
/// that closes an abandoned request's task is a hosted service the Host registers, as it does for every
/// other module's recurring work.
/// </para>
/// </remarks>
public sealed class UpdatesModule : IPanelModule
{
    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Updates.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Updates.Resources.DisplayNames";

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
            return UpdatesManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The paths are the installer's; a server never sets them. The seam exists so a test can point
        // every path at an isolated directory.
        services.AddOptions<PanelUpdateOptions>()
            .Bind(configuration.GetSection(PanelUpdateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singletons: they hold nothing but the options and the clock, and do their file I/O per call.
        // The reaper, which is a singleton, needs them as such.
        services.AddSingleton<PanelUpdateStateReader>();
        services.AddSingleton<PanelUpdateRequestMarker>();
        services.AddSingleton<PanelUpdateTaskLedger>();

        // Scoped, like every journal: it carries ICurrentUser, which is per request.
        services.AddScoped<UpdatesAuditJournal>();
        services.AddScoped<PanelUpdateRequestReporter>();

        // The shared resource pools the panel-wide ResxErrorTextProvider resolves error codes (including
        // the codes a finished update task carries) and Manifest.DisplayNameKey against.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(UpdatesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(UpdatesModule).Assembly));
    }
}
