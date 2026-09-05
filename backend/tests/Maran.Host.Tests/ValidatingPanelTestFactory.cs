using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.Tests;

/// <summary>
/// The panel host built with the container validation a real boot performs: every singleton's
/// dependencies checked at build time, and scoped services refused from the root provider.
/// </summary>
/// <remarks>
/// This exists because <see cref="PanelTestFactory"/> does NOT do that, and the difference is the
/// whole reason a composition defect reached a reviewer's machine with 690 tests green.
///
/// ASP.NET Core enables <c>ValidateOnBuild</c> and <c>ValidateScopes</c> by default only in the
/// Development environment. The shared factory boots as "Testing", so a singleton capturing a scoped
/// dependency — which the container refuses outright on a real start — was silently permitted in
/// every test, and the failure appeared for the first time when the host actually ran.
///
/// Turning both on here makes the test suite ask the question a real boot asks. It is a separate
/// factory rather than a change to the shared one so that the validation is the SUBJECT of these
/// tests rather than an incidental setting other suites inherit.
/// </remarks>
public sealed class ValidatingPanelTestFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(PanelTestSettings.EncryptionKeyPath, PanelTestSettings.EncryptionKey);
        builder.UseSetting(PanelTestSettings.JwtSigningKeyPath, PanelTestSettings.JwtSigningKey);
        builder.UseSetting(PanelTestSettings.FirewallSshPortsPath, PanelTestSettings.FirewallSshPorts);
        builder.UseSetting(PanelTestSettings.FirewallPanelPortPath, PanelTestSettings.FirewallPanelPort);
        builder.UseSetting("Database:Host", string.Empty);

        builder.UseDefaultServiceProvider(options =>
        {
            // Both, and for different failures. ValidateOnBuild walks every registration at build
            // time and refuses a singleton whose constructor asks for a scoped service — the exact
            // check that stops the panel starting. ValidateScopes refuses a scoped service resolved
            // from the root provider at run time, which is the same mistake made later instead.
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
    }
}
